using Aynera.Api.Controllers.Base;
using Aynera.Application.Features.Voice.Models;
using Aynera.Application.Features.Voice.Services.Interfaces;
using Aynera.Domain.Auth.Statics;
using Aynera.Domain.Common;
using Aynera.Domain.Voice.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aynera.Api.Controllers;

/// <summary>
/// Spoken answers to the member's conversation prompts. The prompts themselves — which ones, in
/// what order, and any typed answer — are saved through <c>PATCH members/me/registration</c>
/// (<c>prompts</c>); a prompt counts as answered when it has typed text, a recording, or both.
/// </summary>
[Route("voice-answers")]
[Tags("VoiceAnswers")]
public sealed class VoiceAnswersController : BaseController
{
    private const string StaffTag = "VoiceAnswers · Staff";

    private readonly IVoiceAnswerService _voice;

    public VoiceAnswersController(IVoiceAnswerService voice)
    {
        _voice = voice;
    }

    /// <summary>UploadVoiceAnswer</summary>
    /// <remarks>
    /// Stores (or replaces) the spoken answer to one prompt. Multipart form: <c>promptId</c> (a prompt
    /// already in the member's chosen list, else <c>voice_prompt_not_chosen</c>) and the file in
    /// <c>audio</c>. Allowed: M4A/AAC (<c>audio/mp4</c>, <c>audio/m4a</c>, <c>audio/aac</c>), MP3, WebM and
    /// Ogg, at most 5 MB. The app caps a recording at 60 seconds. Stored at
    /// <c>{userId}/voice_{promptId}.{ext}</c>; <c>url</c> is a signed playback link valid for an hour.
    /// </remarks>
    [HttpPost("Upload")]
    [Authorize(Policy = AuthPolicies.Member)]
    [RequestSizeLimit(8 * 1024 * 1024)]
    [ProducesResponseType(typeof(ApiResponse<VoiceAnswerDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<VoiceAnswerDto>>> UploadVoiceAnswer(
        CancellationToken cancellationToken)
    {
        var file = Request.Form.Files.GetFile("audio") ?? Request.Form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
            return FailResponse<VoiceAnswerDto>("voice_required");
        }

        await using var stream = file.OpenReadStream();
        var dto = await _voice.UploadAsync(
            CurrentUser.UserId!.Value,
            new VoiceUploadInput(
                stream,
                file.ContentType,
                file.Length,
                Request.Form.TryGetValue("promptId", out var promptId) ? promptId.ToString() : null),
            cancellationToken);

        return OkResponse(dto);
    }

    /// <summary>ListVoiceAnswers</summary>
    /// <remarks>The member's stored recordings, each with a signed playback link.</remarks>
    [HttpGet("GetAll")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<VoiceAnswerDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<VoiceAnswerDto>>>> ListVoiceAnswers(
        CancellationToken cancellationToken) =>
        OkResponse(await _voice.ListAsync(CurrentUser.UserId!.Value, cancellationToken));

    /// <summary>DeleteVoiceAnswer</summary>
    /// <remarks>Removes the recording for one prompt. A typed answer to the same prompt is unaffected.</remarks>
    [HttpDelete("{promptId}")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteVoiceAnswer(
        string promptId,
        CancellationToken cancellationToken)
    {
        await _voice.DeleteAsync(CurrentUser.UserId!.Value, promptId, cancellationToken);
        return OkResponse();
    }

    // ----- Staff (admin panel) -----

    /// <summary>GetMemberVoiceAnswer</summary>
    /// <remarks>Returns one member's recording for a prompt, for review. Any admin.</remarks>
    [HttpGet("{id:guid}/{promptId}/content")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [Tags(StaffTag)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMemberVoiceAnswer(
        Guid id,
        string promptId,
        CancellationToken cancellationToken)
    {
        var answer = await _voice.GetBytesAsync(id, promptId, cancellationToken);
        return File(answer.Data, answer.ContentType);
    }
}
