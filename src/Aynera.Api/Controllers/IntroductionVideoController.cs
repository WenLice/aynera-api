using Aynera.Api.Controllers.Base;
using Aynera.Application.Features.Users.Services.Interfaces;
using Aynera.Application.Features.Videos.Models;
using Aynera.Application.Features.Videos.Services.Interfaces;
using Aynera.Domain.Auth.Statics;
using Aynera.Domain.Common;
using Aynera.Domain.Videos.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aynera.Api.Controllers;

/// <summary>
/// Member introduction video: the signed-in member's own video (<c>introduction-video/me*</c>, upload via
/// <c>introduction-video/Upload</c>) and staff reads of a member's video bytes (<c>introduction-video/{id}/content</c>).
/// Profile photos live on <c>profile/*</c>.
/// </summary>
[Route("introduction-video")]
[Tags("IntroductionVideo")]
public sealed class IntroductionVideoController : BaseController
{
    private const string StaffTag = "IntroductionVideo · Staff";

    private readonly IIntroductionVideoService _introductionVideoService;
    private readonly IUserManagementService _userManagement;

    public IntroductionVideoController(
        IIntroductionVideoService introductionVideoService,
        IUserManagementService userManagement)
    {
        _introductionVideoService = introductionVideoService;
        _userManagement = userManagement;
    }

    /// <summary>UploadIntroductionVideo</summary>
    /// <remarks>
    /// Uploads (or replaces) the member introduction video (multipart form field <c>video</c>).
    /// Requires a reference profile photo. Face is matched against that photo (stub in dev).
    /// Speech is checked against community banned-words guidelines (option C); violation rejects upload.
    /// AI-generated or synthetic media is rejected (<c>video_ai_generated</c>).
    /// Allowed: MP4 / WebM / QuickTime, max size from config (default 25 MB).
    /// </remarks>
    [HttpPost("Upload")]
    [Authorize(Policy = AuthPolicies.Member)]
    [RequestSizeLimit(40 * 1024 * 1024)]
    [ProducesResponseType(typeof(ApiResponse<IntroductionVideoDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IntroductionVideoDto>>> UploadIntroductionVideo(
        CancellationToken cancellationToken)
    {
        var file = Request.Form.Files.GetFile("video")
            ?? Request.Form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
            return FailResponse<IntroductionVideoDto>("video_required");
        }

        await using var stream = file.OpenReadStream();
        var dto = await _introductionVideoService.UploadAsync(
            CurrentUser.UserId!.Value,
            new VideoUploadInput(stream, file.FileName, file.ContentType, file.Length),
            cancellationToken);

        return OkResponse(dto);
    }

    /// <summary>GetIntroductionVideo</summary>
    /// <remarks>Returns metadata for the authenticated member's introduction video (no bytes).</remarks>
    [HttpGet("me")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(ApiResponse<IntroductionVideoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IntroductionVideoDto>>> GetIntroductionVideo(
        CancellationToken cancellationToken)
    {
        var video = await _introductionVideoService.GetAsync(CurrentUser.UserId!.Value, cancellationToken);
        if (video is null)
        {
            return FailResponse<IntroductionVideoDto>("video_not_found", StatusCodes.Status404NotFound);
        }

        return OkResponse(video);
    }

    /// <summary>GetIntroductionVideoContent</summary>
    /// <remarks>Returns the authenticated member's introduction video bytes.</remarks>
    [HttpGet("me/content")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetIntroductionVideoContent(CancellationToken cancellationToken)
    {
        var video = await _introductionVideoService.GetBytesAsync(CurrentUser.UserId!.Value, cancellationToken);
        return File(video.Data, video.ContentType);
    }

    /// <summary>DeleteIntroductionVideo</summary>
    [HttpDelete("me")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteIntroductionVideo(
        CancellationToken cancellationToken)
    {
        await _introductionVideoService.DeleteAsync(CurrentUser.UserId!.Value, cancellationToken);
        return OkResponse();
    }

    // ----- Staff (admin panel) -----

    /// <summary>GetMemberVideo</summary>
    /// <remarks>Returns introduction-video bytes for a member. Any admin. Video metadata is on <c>GET members/{id}</c>.</remarks>
    [HttpGet("{id:guid}/content")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [Tags(StaffTag)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMemberVideo(Guid id, CancellationToken cancellationToken)
    {
        var video = await _userManagement.GetMemberVideoAsync(
            CurrentUser.GetRequiredUserId(),
            id,
            cancellationToken);
        return File(video.Data, video.ContentType);
    }
}
