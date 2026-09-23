using Aynera.Domain.Media.Requests;
using Aynera.Api.Controllers.Base;
using Aynera.Application.Features.Photos.Models;
using Aynera.Application.Features.Photos.Services.Interfaces;
using Aynera.Application.Features.Users.Services.Interfaces;
using Aynera.Domain.Auth.Statics;
using Aynera.Domain.Common;
using Aynera.Domain.Photos.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aynera.Api.Controllers;

/// <summary>
/// Member profile photos: the signed-in member's own photos (<c>photos/Upload</c>, <c>photos/GetAll</c>,
/// <c>photos/{photoId}</c>) and staff reads of a member's photo bytes (<c>photos/{id}/{photoId}</c>).
/// The introduction video lives on <c>introduction-video/*</c>; account fields stay on <c>members/me</c>.
/// </summary>
[Route("photos")]
[Tags("Photos")]
public sealed class PhotosController : BaseController
{
    private const string StaffTag = "Photos · Staff";

    private readonly IPhotoService _photoService;
    private readonly IUserManagementService _userManagement;

    public PhotosController(IPhotoService photoService, IUserManagementService userManagement)
    {
        _photoService = photoService;
        _userManagement = userManagement;
    }

    /// <summary>UploadPhotos</summary>
    /// <remarks>
    /// Uploads one or more profile photos (multipart form field <c>photos</c>).
    /// Max 5 photos per account, 5 MB each; JPEG/PNG/WebP accepted, re-encoded as JPEG with location and camera
    /// metadata stripped, and stored in object storage (Cloudflare R2) at <c>{userId}/photo_{slot}.jpg</c>.
    /// Each photo takes the lowest free slot, so a deleted slot is refilled before a new number is used.
    /// A one-file upload may name its <c>slot</c> (1–5) and <c>caption</c> as form fields; a slot that already
    /// holds a photo is replaced. <c>url</c> in the response is a signed link valid for an hour.
    /// The first photo becomes the reference selfie. Later uploads are compared via the face-match service (stub in dev).
    /// AI-generated or synthetic media is rejected (<c>photo_ai_generated</c>) to keep identity verification trustworthy.
    /// </remarks>
    [HttpPost("Upload")]
    [Authorize(Policy = AuthPolicies.Member)]
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

        // "slot" and "caption" describe a single photo, so they only apply to a one-file upload —
        // which is how the app sends them, one slot at a time.
        int? slot = null;
        string? caption = null;
        if (files.Count == 1)
        {
            if (int.TryParse(Request.Form["slot"].ToString(), out var parsed))
            {
                slot = parsed;
            }

            caption = Request.Form.TryGetValue("caption", out var captionValue) ? captionValue.ToString() : null;
        }

        var inputs = files
            .Where(f => f.Length > 0)
            .Select(f => new PhotoUploadInput(f.OpenReadStream(), f.FileName, f.ContentType, f.Length, slot, caption))
            .ToList();

        var created = await _photoService.UploadAsync(CurrentUser.UserId!.Value, inputs, cancellationToken);
        return OkResponse(created);
    }

    /// <summary>ListPhotos</summary>
    /// <remarks>Returns metadata for the authenticated member's photos (no image bytes).</remarks>
    [HttpGet("GetAll")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MemberPhotoDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MemberPhotoDto>>>> ListPhotos(
        CancellationToken cancellationToken)
    {
        var photos = await _photoService.ListAsync(CurrentUser.UserId!.Value, cancellationToken);
        return OkResponse(photos);
    }

    /// <summary>GetPhoto</summary>
    /// <remarks>Returns the image bytes for one of the authenticated member's photos.</remarks>
    [HttpGet("{photoId:guid}")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPhoto(Guid photoId, CancellationToken cancellationToken)
    {
        var photo = await _photoService.GetBytesAsync(CurrentUser.UserId!.Value, photoId, cancellationToken);
        return File(photo.Data, photo.ContentType);
    }

    /// <summary>UpdatePhotoCaption</summary>
    /// <remarks>
    /// Sets or clears (null or blank) the caption of one of the authenticated member's photos without
    /// re-uploading the image. At most 200 characters.
    /// </remarks>
    [HttpPatch("{photoId:guid}")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<object?>>> UpdatePhotoCaption(
        Guid photoId,
        [FromBody] UpdateMediaCaptionRequest request,
        CancellationToken cancellationToken)
    {
        await _photoService.UpdateCaptionAsync(CurrentUser.UserId!.Value, photoId, request.Caption, cancellationToken);
        return OkResponse();
    }

    /// <summary>DeletePhoto</summary>
    /// <remarks>Soft-deletes a photo. If it was the reference, the next remaining photo is promoted.</remarks>
    [HttpDelete("{photoId:guid}")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object?>>> DeletePhoto(
        Guid photoId,
        CancellationToken cancellationToken)
    {
        await _photoService.DeleteAsync(CurrentUser.UserId!.Value, photoId, cancellationToken);
        return OkResponse();
    }

    // ----- Staff (admin panel) -----

    /// <summary>GetMemberPhoto</summary>
    /// <remarks>Returns image bytes for one photo on a member. Any admin. Photo metadata is on <c>GET members/{id}</c>.</remarks>
    [HttpGet("{id:guid}/{photoId:guid}")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [Tags(StaffTag)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMemberPhoto(
        Guid id,
        Guid photoId,
        CancellationToken cancellationToken)
    {
        var photo = await _userManagement.GetMemberPhotoAsync(
            CurrentUser.GetRequiredUserId(),
            id,
            photoId,
            cancellationToken);
        return File(photo.Data, photo.ContentType);
    }
}
