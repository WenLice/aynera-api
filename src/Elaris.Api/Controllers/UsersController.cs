using Elaris.Api.Controllers.Base;
using Elaris.Application.Features.Auth.Services.Interfaces;
using Elaris.Application.Features.Photos.Models;
using Elaris.Application.Features.Photos.Services.Interfaces;
using Elaris.Application.Features.Videos.Models;
using Elaris.Application.Features.Videos.Services.Interfaces;
using Elaris.Domain.Auth.Requests;
using Elaris.Domain.Auth.Responses;
using Elaris.Domain.Common;
using Elaris.Domain.Photos.Responses;
using Elaris.Domain.Videos.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Elaris.Api.Controllers;

/// <summary>
/// Member account (register, profile, photos, introduction video, deactivate, reactivate, delete).
/// </summary>
[Route("users")]
[Tags("Users")]
public sealed class UsersController : BaseController
{
    private readonly IAuthService _authService;
    private readonly IPhotoService _photoService;
    private readonly IIntroductionVideoService _introductionVideoService;

    public UsersController(
        IAuthService authService,
        IPhotoService photoService,
        IIntroductionVideoService introductionVideoService)
    {
        _authService = authService;
        _photoService = photoService;
        _introductionVideoService = introductionVideoService;
    }

    /// <summary>Register</summary>
    /// <remarks>
    /// Creates a new member account and profile.
    /// Required: phone, firstName, lastName, gender (Male|Female|Other), dateOfBirth (18+), city, email.
    /// Optional: religion. Profile photos are uploaded separately via UploadPhotos.
    /// Blocked if an active or deactivated account already uses that phone.
    /// Soft-deleted accounts do not block — a new account id is created.
    /// Sends an email verification link automatically. <c>emailConfirmed</c> stays false until Auth VerifyEmail.
    /// The link opens member-web (or a landing page); that page should POST to <c>/auth/verifyemail</c> with userId + token.
    /// Does not issue tokens; call Auth Login then VerifySms to confirm the phone and sign in.
    /// </remarks>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AuthAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<AuthAccountDto>>> Register(
        [FromBody] CreateMemberRequest request,
        CancellationToken cancellationToken)
    {
        var account = await _authService.CreateMemberAsync(request, cancellationToken);
        return OkResponse(account);
    }

    /// <summary>Me</summary>
    /// <remarks>
    /// Returns the authenticated member account for the current access token.
    /// Requires Bearer JWT and the Member policy.
    /// </remarks>
    [HttpGet("me")]
    [Authorize(Policy = "Member")]
    [ProducesResponseType(typeof(ApiResponse<AuthAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<AuthAccountDto>>> Me(CancellationToken cancellationToken)
    {
        var account = await _authService.GetMeAsync(CurrentUser.UserId!.Value, cancellationToken);
        return OkResponse(account);
    }

    /// <summary>UploadPhotos</summary>
    /// <remarks>
    /// Uploads one or more profile photos (multipart form field <c>photos</c>).
    /// Max 6 photos per account, 5 MB each; JPEG/PNG/WebP accepted and stored as compressed JPEG in Postgres.
    /// The first photo becomes the reference selfie. Later uploads are compared via the face-match service (stub in dev).
    /// AI-generated or synthetic media is rejected (<c>photo_ai_generated</c>) to keep identity verification trustworthy.
    /// </remarks>
    [HttpPost("me/photos")]
    [Authorize(Policy = "Member")]
    [RequestSizeLimit(40 * 1024 * 1024)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MemberPhotoDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MemberPhotoDto>>>> UploadPhotos(
        CancellationToken cancellationToken)
    {
        var files = Request.Form.Files
            .Where(f => string.Equals(f.Name, "photos", StringComparison.OrdinalIgnoreCase)
                        || string.IsNullOrEmpty(f.Name))
            .ToList();
        if (files.Count == 0 && Request.Form.Files.Count > 0)
        {
            files = Request.Form.Files.ToList();
        }

        var inputs = files
            .Where(f => f.Length > 0)
            .Select(f => new PhotoUploadInput(f.OpenReadStream(), f.FileName, f.ContentType, f.Length))
            .ToList();

        var created = await _photoService.UploadAsync(CurrentUser.UserId!.Value, inputs, cancellationToken);
        return OkResponse(created);
    }

    /// <summary>ListPhotos</summary>
    /// <remarks>Returns metadata for the authenticated member's photos (no image bytes).</remarks>
    [HttpGet("me/photos")]
    [Authorize(Policy = "Member")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MemberPhotoDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MemberPhotoDto>>>> ListPhotos(
        CancellationToken cancellationToken)
    {
        var photos = await _photoService.ListAsync(CurrentUser.UserId!.Value, cancellationToken);
        return OkResponse(photos);
    }

    /// <summary>GetPhoto</summary>
    /// <remarks>Returns the image bytes for one of the authenticated member's photos.</remarks>
    [HttpGet("me/photos/{photoId:guid}")]
    [Authorize(Policy = "Member")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPhoto(Guid photoId, CancellationToken cancellationToken)
    {
        var photo = await _photoService.GetBytesAsync(CurrentUser.UserId!.Value, photoId, cancellationToken);
        return File(photo.Data, photo.ContentType);
    }

    /// <summary>DeletePhoto</summary>
    /// <remarks>Soft-deletes a photo. If it was the reference, the next remaining photo is promoted.</remarks>
    [HttpDelete("me/photos/{photoId:guid}")]
    [Authorize(Policy = "Member")]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object?>>> DeletePhoto(
        Guid photoId,
        CancellationToken cancellationToken)
    {
        await _photoService.DeleteAsync(CurrentUser.UserId!.Value, photoId, cancellationToken);
        return OkResponse();
    }

    /// <summary>UploadIntroductionVideo</summary>
    /// <remarks>
    /// Uploads (or replaces) the member introduction video.
    /// Requires a reference profile photo. Face is matched against that photo (stub in dev).
    /// Speech is checked against community banned-words guidelines (option C); violation rejects upload.
    /// AI-generated or synthetic media is rejected (<c>video_ai_generated</c>).
    /// Allowed: MP4 / WebM / QuickTime, max size from config (default 25 MB).
    /// </remarks>
    [HttpPost("me/introduction-video")]
    [Authorize(Policy = "Member")]
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
    /// <remarks>Returns metadata for the introduction video (no bytes).</remarks>
    [HttpGet("me/introduction-video")]
    [Authorize(Policy = "Member")]
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
    /// <remarks>Returns the introduction video bytes.</remarks>
    [HttpGet("me/introduction-video/content")]
    [Authorize(Policy = "Member")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetIntroductionVideoContent(CancellationToken cancellationToken)
    {
        var video = await _introductionVideoService.GetBytesAsync(CurrentUser.UserId!.Value, cancellationToken);
        return File(video.Data, video.ContentType);
    }

    /// <summary>DeleteIntroductionVideo</summary>
    [HttpDelete("me/introduction-video")]
    [Authorize(Policy = "Member")]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteIntroductionVideo(
        CancellationToken cancellationToken)
    {
        await _introductionVideoService.DeleteAsync(CurrentUser.UserId!.Value, cancellationToken);
        return OkResponse();
    }

    /// <summary>Deactivate</summary>
    /// <remarks>
    /// Deactivates the authenticated account (IsActive = false).
    /// Login and re-registration are blocked until Reactivate.
    /// Refresh sessions for the user are revoked.
    /// </remarks>
    [HttpPost("deactivate")]
    [Authorize(Policy = "Member")]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object?>>> Deactivate(CancellationToken cancellationToken)
    {
        await _authService.DeactivateMemberAsync(CurrentUser.UserId!.Value, cancellationToken);
        return OkResponse();
    }

    /// <summary>Reactivate</summary>
    /// <remarks>
    /// Reactivates a deactivated account (IsActive = true).
    /// Soft-deleted accounts cannot be reactivated — Register again instead.
    /// Requires a still-valid access token from before deactivation (or a path that can obtain one).
    /// </remarks>
    [HttpPost("reactivate")]
    [Authorize(Policy = "Member")]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object?>>> Reactivate(CancellationToken cancellationToken)
    {
        await _authService.ActivateMemberAsync(CurrentUser.UserId!.Value, cancellationToken);
        return OkResponse();
    }

    /// <summary>DeleteAccount</summary>
    /// <remarks>
    /// Soft-deletes the authenticated account and related data (for example refresh sessions and photos).
    /// The same phone may Register again later as a brand-new account; old data stays inaccessible.
    /// </remarks>
    [HttpDelete("account")]
    [Authorize(Policy = "Member")]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteAccount(CancellationToken cancellationToken)
    {
        await _authService.DeleteMemberAsync(CurrentUser.UserId!.Value, cancellationToken);
        return OkResponse();
    }
}
