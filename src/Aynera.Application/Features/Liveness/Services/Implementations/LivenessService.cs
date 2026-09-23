using Aynera.Application.Features.Audit.Services.Interfaces;
using Aynera.Application.Features.Liveness.Models;
using Aynera.Application.Features.Liveness.Repositories;
using Aynera.Application.Features.Liveness.Services.Interfaces;
using Aynera.Domain.Audit.Records;
using Aynera.Domain.Audit.Statics;
using Aynera.Domain.Liveness.Enums;
using Aynera.Domain.Liveness.Exceptions;
using Aynera.Domain.Liveness.Records;
using Aynera.Domain.Liveness.Responses;
using Aynera.Domain.Photos.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aynera.Application.Features.Liveness.Services.Implementations;

/// <summary>
/// The private face check: is this a live person? It comes first in registration, and the frame it
/// captures becomes the member's verified face — the anchor photos and the intro video are matched
/// against, so nobody can build a profile out of someone else's pictures.
/// <para>
/// The page the member sees only streams video to the provider and says "done". Every verdict is
/// read here, from the provider, with the server's own credentials — a tampered page can report
/// nothing but that it finished.
/// </para>
/// </summary>
public sealed class LivenessService : ILivenessService
{
    private readonly IFaceLivenessProvider _provider;
    private readonly ILivenessRepository _liveness;
    private readonly IAuditWriter _audit;
    private readonly ILogger<LivenessService> _logger;
    private readonly LivenessOptions _options;

    public LivenessService(
        IFaceLivenessProvider provider,
        ILivenessRepository liveness,
        IAuditWriter audit,
        ILogger<LivenessService> logger,
        IOptions<LivenessOptions> options)
    {
        _provider = provider;
        _liveness = liveness;
        _audit = audit;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<LivenessStartDto> StartAsync(Guid userId, CancellationToken cancellationToken)
    {
        var sessionId = await _provider.CreateSessionAsync(cancellationToken);
        await _liveness.AddSessionAsync(
            new LivenessSessionRecord(
                sessionId,
                userId,
                LivenessOutcome.Pending.ToString(),
                Confidence: null,
                Similarity: null,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                CompletedAtUtc: null),
            cancellationToken);

        _logger.LogInformation("Liveness session {SessionId} started for {UserId}", sessionId, userId);

        var page = _options.PageUrl
            + (_options.PageUrl.Contains('?') ? "&" : "?")
            + $"session={Uri.EscapeDataString(sessionId)}"
            + $"&region={Uri.EscapeDataString(_provider.Region)}"
            + $"&pool={Uri.EscapeDataString(_provider.IdentityPoolId)}";

        return new LivenessStartDto(sessionId, _provider.Region, page);
    }

    public async Task<LivenessResultDto> CompleteAsync(
        Guid userId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var session = await _liveness.FindSessionAsync(sessionId, cancellationToken);

        // Another member's session reads exactly like a missing one: nothing to learn from probing.
        if (session is null || session.UserId != userId)
        {
            throw new LivenessException("liveness_session_not_found", "Face check not found.", statusCode: 404);
        }

        if (session.Outcome != LivenessOutcome.Pending.ToString())
        {
            return ToDto(session);
        }

        var result = await _provider.GetResultAsync(sessionId, cancellationToken);
        var outcome = result.Status.ToUpperInvariant() switch
        {
            "CREATED" or "IN_PROGRESS" => throw new LivenessException(
                "liveness_not_finished",
                "The face check hasn't finished yet.",
                statusCode: 409),
            "EXPIRED" => LivenessOutcome.Expired,
            "SUCCEEDED" => (LivenessOutcome?)null,
            _ => LivenessOutcome.Failed,
        };

        decimal? similarity = null;
        if (outcome is null)
        {
            outcome = result.Confidence is decimal confidence && confidence >= _options.MinConfidence
                && result.ReferenceImage is { Length: > 0 }
                ? LivenessOutcome.Passed
                : LivenessOutcome.NotLive;

            if (result.ReferenceImage is { Length: > 0 } frame)
            {
                // Kept either way: a pass makes it the member's verified face; a fail is there for a
                // curator to look at. Only the latest check counts, so a failed re-check also
                // withdraws an earlier verified face.
                await _liveness.SaveSelfieAsync(
                    userId,
                    frame,
                    outcome == LivenessOutcome.Passed ? FaceMatchStatus.Matched.ToString() : FaceMatchStatus.Pending.ToString(),
                    similarity: null,
                    cancellationToken);
            }
        }

        var completed = session with
        {
            Outcome = outcome.Value.ToString(),
            Confidence = result.Confidence,
            Similarity = similarity,
            CompletedAtUtc = DateTimeOffset.UtcNow,
        };
        await _liveness.UpdateSessionAsync(completed, cancellationToken);

        await _audit.WriteAsync(
            new AuditEventWriteModel(
                Action: AuditActions.LivenessChecked,
                Outcome: outcome == LivenessOutcome.Passed ? AuditOutcomes.Success : AuditOutcomes.Failure,
                Message: $"Face check: {outcome}.",
                UserId: userId,
                SubjectUserId: userId,
                SubjectType: AuditSubjectTypes.LivenessSession,
                SubjectId: sessionId,
                Metadata: new { outcome = outcome.ToString(), confidence = result.Confidence, similarity }),
            cancellationToken);

        _logger.LogInformation(
            "Liveness session {SessionId} for {UserId}: {Outcome} (confidence {Confidence}, similarity {Similarity})",
            sessionId, userId, outcome, result.Confidence, similarity);

        return ToDto(completed);
    }

    public async Task<LivenessResultDto?> GetLatestAsync(Guid userId, CancellationToken cancellationToken)
    {
        var latest = await _liveness.FindLatestCompletedAsync(userId, cancellationToken);
        return latest is null ? null : ToDto(latest);
    }

    private static LivenessResultDto ToDto(LivenessSessionRecord session) =>
        new(
            session.SessionId,
            session.Outcome,
            session.Outcome == LivenessOutcome.Passed.ToString(),
            session.Confidence,
            session.Similarity,
            session.CompletedAtUtc);
}
