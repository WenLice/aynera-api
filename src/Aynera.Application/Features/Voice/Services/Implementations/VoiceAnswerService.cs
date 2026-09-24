using Aynera.Application.Features.Answers.Repositories;
using Aynera.Application.Features.Audit.Services.Interfaces;
using Aynera.Application.Features.Media.Storage;
using Aynera.Application.Features.Videos.Services.Interfaces;
using Aynera.Application.Features.Voice.Models;
using Aynera.Application.Features.Voice.Repositories;
using Aynera.Application.Features.Voice.Services.Interfaces;
using Aynera.Domain.Answers.Statics;
using Aynera.Domain.Audit.Records;
using Aynera.Domain.Audit.Statics;
using Aynera.Domain.Voice.Exceptions;
using Aynera.Domain.Voice.Records;
using Aynera.Domain.Voice.Responses;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aynera.Application.Features.Voice.Services.Implementations;

/// <summary>
/// Spoken answers to the member's conversation prompts. A prompt can be answered by typing
/// (stored with the prompt list through the registration endpoint) or by recording (stored here);
/// either counts.
/// <para>
/// A recording is only accepted for a prompt the member currently holds, so the bucket can never
/// carry an answer to a question nobody sees. Changing the prompt list removes recordings for the
/// prompts dropped from it.
/// </para>
/// </summary>
public sealed class VoiceAnswerService : IVoiceAnswerService
{
    /// <summary>How long a signed playback link stays valid.</summary>
    private static readonly TimeSpan ReadUrlLifetime = TimeSpan.FromHours(1);

    /// <summary>
    /// Phones record AAC in an MP4 container; browsers record WebM/Opus (Chrome, Firefox) or MP4
    /// (Safari). Uncompressed formats are left out on purpose — a minute of WAV is ~10 MB.
    /// </summary>
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio/mp4",
        "audio/m4a",
        "audio/x-m4a",
        "audio/aac",
        "audio/mpeg",
        "audio/webm",
        "audio/ogg",
    };

    private readonly IVoiceAnswerRepository _voice;
    private readonly IMemberProfileAnswersRepository _answers;
    private readonly ISpeechTranscriptionService _transcription;
    private readonly ISpeechGuidelineService _guidelines;
    private readonly IMediaStorage _storage;
    private readonly IAuditWriter _audit;
    private readonly ILogger<VoiceAnswerService> _logger;
    private readonly VoiceAnswerOptions _options;

    public VoiceAnswerService(
        IVoiceAnswerRepository voice,
        IMemberProfileAnswersRepository answers,
        ISpeechTranscriptionService transcription,
        ISpeechGuidelineService guidelines,
        IMediaStorage storage,
        IAuditWriter audit,
        ILogger<VoiceAnswerService> logger,
        IOptions<VoiceAnswerOptions> options)
    {
        _voice = voice;
        _answers = answers;
        _transcription = transcription;
        _guidelines = guidelines;
        _storage = storage;
        _audit = audit;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<VoiceAnswerDto> UploadAsync(
        Guid userId,
        VoiceUploadInput file,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "UploadVoiceAnswer user {UserId} prompt {PromptId} bytes {Length}", userId, file.PromptId, file.Length);

        var promptId = file.PromptId?.Trim() ?? string.Empty;
        if (!PromptRules.IsValidPromptId(promptId))
        {
            throw new VoiceAnswerException("voice_prompt_invalid", "Choose which prompt this answers.");
        }

        if (file.Length <= 0)
        {
            throw new VoiceAnswerException("voice_empty", "The recording is empty.");
        }

        if (file.Length > _options.MaxBytes)
        {
            throw new VoiceAnswerException(
                "voice_too_large",
                $"A recording can be at most {_options.MaxBytes / (1024 * 1024)} MB.");
        }

        var contentType = (file.ContentType ?? string.Empty).Split(';', 2)[0].Trim().ToLowerInvariant();
        if (!AllowedContentTypes.Contains(contentType))
        {
            throw new VoiceAnswerException(
                "voice_unsupported_type",
                "Only M4A, AAC, MP3, WebM and Ogg recordings are allowed.");
        }

        var answers = await _answers.FindByUserIdAsync(userId, cancellationToken);
        if (answers is null || answers.Prompts.All(p => !string.Equals(p.PromptId, promptId, StringComparison.Ordinal)))
        {
            throw new VoiceAnswerException(
                "voice_prompt_not_chosen",
                "Choose this prompt before recording an answer to it.");
        }

        await using var buffer = new MemoryStream();
        await file.Content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        buffer.Position = 0;
        var transcript = await _transcription.TranscribeAsync(buffer, contentType, cancellationToken);
        var guideline = _guidelines.Evaluate(transcript);
        if (!guideline.Passed)
        {
            throw new VoiceAnswerException(
                "voice_guideline_failed",
                guideline.Detail ?? "This recording does not meet community guidelines.");
        }

        var record = await _voice.UpsertAsync(
            new VoiceAnswerRecord(
                UserId: userId,
                PromptId: promptId,
                ContentType: contentType,
                ByteSize: bytes.Length,
                Data: bytes,
                GuidelinePassed: true,
                GuidelineDetail: guideline.Detail,
                Transcript: string.IsNullOrWhiteSpace(transcript) ? null : transcript.Trim(),
                CreatedAtUtc: DateTimeOffset.UtcNow,
                UpdatedAtUtc: null),
            cancellationToken);

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.VoiceAnswerUploaded,
                Outcome: AuditOutcomes.Success,
                Message: "Voice answer uploaded.",
                UserId: userId,
                SubjectUserId: userId,
                SubjectType: AuditSubjectTypes.VoiceAnswer,
                SubjectId: promptId,
                Metadata: new { promptId, contentType, byteSize = bytes.Length }),
            cancellationToken);

        return ToDto(record);
    }

    public async Task<IReadOnlyList<VoiceAnswerDto>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        (await _voice.ListByUserIdAsync(userId, cancellationToken)).Select(ToDto).ToList();

    public async Task<VoiceAnswerBytes> GetBytesAsync(
        Guid userId,
        string promptId,
        CancellationToken cancellationToken)
    {
        var answer = await _voice.FindWithContentAsync(userId, promptId, cancellationToken);

        // A row whose file is missing from the bucket is reported as not found rather than served
        // as an empty recording the player would fail on silently.
        if (answer is null || answer.Data.Length == 0)
        {
            throw new VoiceAnswerException("voice_not_found", "Recording not found.", statusCode: 404);
        }

        return new VoiceAnswerBytes(answer.UserId, answer.PromptId, answer.ContentType, answer.Data);
    }

    public async Task DeleteAsync(Guid userId, string promptId, CancellationToken cancellationToken)
    {
        if (!await _voice.SoftDeleteAsync(userId, promptId, cancellationToken))
        {
            throw new VoiceAnswerException("voice_not_found", "Recording not found.", statusCode: 404);
        }

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.VoiceAnswerDeleted,
                Outcome: AuditOutcomes.Success,
                Message: "Voice answer deleted.",
                UserId: userId,
                SubjectUserId: userId,
                SubjectType: AuditSubjectTypes.VoiceAnswer,
                SubjectId: promptId),
            cancellationToken);
    }

    private VoiceAnswerDto ToDto(VoiceAnswerRecord record) =>
        new(
            record.PromptId,
            record.ContentType,
            record.ByteSize,
            string.IsNullOrEmpty(record.StorageKey) ? null : _storage.GetReadUrl(record.StorageKey, ReadUrlLifetime),
            record.CreatedAtUtc,
            record.UpdatedAtUtc);
}
