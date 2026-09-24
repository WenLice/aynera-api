using Aynera.Domain.Auth.Statics;
using Aynera.Application.Features.Users.Services.Interfaces;
using Aynera.Api.Controllers.Base;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Application.Features.Registration.Services.Interfaces;
using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Responses;
using Aynera.Domain.Common;
using Aynera.Domain.Registration.Requests;
using Aynera.Domain.Registration.Responses;
using Aynera.Application.Features.Settings.Services.Interfaces;
using Aynera.Domain.Settings.Requests;
using Aynera.Domain.Settings.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aynera.Api.Controllers;

/// <summary>
/// Member accounts: registration, the authenticated member's own account and lifecycle (<c>members/me/*</c>),
/// anonymous account recovery, and staff views/moderation of members (<c>members/{id}/*</c>).
/// Profile media (photos, introduction video) lives on <c>profile/*</c>.
/// </summary>
[Route("members")]
[Tags("Members")]
public sealed class MembersController : BaseController
{
    private const string StaffTag = "Members · Staff";

    private readonly IAccountLifecycleService _lifecycle;
    private readonly IRegistrationService _registration;
    private readonly IRegistrationDraftService _draft;
    private readonly IUserManagementService _userManagement;
    private readonly IAuthService _authService;
    private readonly IMemberSettingsService _settings;

    public MembersController(
        IAuthService authService,
        IUserManagementService userManagement,
        IRegistrationService registration,
        IRegistrationDraftService draft,
        IAccountLifecycleService lifecycle,
        IMemberSettingsService settings)
    {
        _settings = settings;
        _authService = authService;
        _userManagement = userManagement;
        _registration = registration;
        _draft = draft;
        _lifecycle = lifecycle;
    }

    /// <summary>Register</summary>
    /// <remarks>
    /// Creates a new member account and profile.
    /// Required: phone, name, gender (Male|Female|ThirdGender), dateOfBirth (18+), city, email.
    /// <c>city</c> must be an active city in the shared city catalog (<c>GET /early-access/cities/GetAll</c>);
    /// the canonical catalog name and its <c>cityId</c> are stored. Unknown or closed cities fail with <c>city_not_supported</c>.
    /// Optional: nickname, heightCm, hometown, work, religion. Photos are uploaded separately via Photos Upload.
    /// Blocked if an active or deactivated account already uses that phone.
    /// Soft-deleted accounts do not block — a new account id is created.
    /// Queues an email verification link for automatic delivery with retries. <c>emailConfirmed</c> stays false until Auth VerifyEmail.
    /// The link opens the member app (or a landing page); that page should POST to <c>/auth/verifyemail</c> with userId + token.
    /// Does not issue tokens; call Auth OtpRequest then OtpVerify to confirm the phone or email and sign in,
    /// or Auth PasswordLogin if a password was provided.
    /// Optional <c>password</c>: at least 8 characters with a lowercase letter and a number.
    /// </remarks>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AuthAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<AuthAccountDto>>> Register(
        [FromBody] CreateMemberRequest request,
        CancellationToken cancellationToken)
    {
        var account = await _registration.RegisterAsync(request, cancellationToken);
        return OkResponse(account);
    }

    /// <summary>StartPhoneRegistration</summary>
    /// <remarks>
    /// App registration, step 1: sends a six-digit SMS code to a mobile number that has no account yet.
    /// Rate limited like login (<c>otp_rate_limited</c>, 429). A number that already belongs to an account
    /// fails with <c>user_already_exists</c> (409) — sign in instead; deactivated / restricted accounts
    /// return <c>account_deactivated</c> / <c>account_restricted</c> (409).
    /// Next step: VerifyPhoneRegistration with the same number and the code.
    /// </remarks>
    [HttpPost("register/phone")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<RequestMemberOtpResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse<RequestMemberOtpResponse>>> StartPhoneRegistration(
        [FromBody] StartPhoneRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _registration.StartPhoneRegistrationAsync(
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);
        return OkResponse(result);
    }

    /// <summary>VerifyPhoneRegistration</summary>
    /// <remarks>
    /// App registration, step 2: verifies the SMS code, creates a <b>Draft</b> member account
    /// (phone confirmed; no email, profile or password yet) and issues member tokens so the remaining
    /// steps save against <c>members/me</c>. The account's admission starts as Draft.
    /// Wrong / expired / locked codes: <c>otp_invalid</c>, <c>otp_expired</c>, <c>otp_locked</c> (401).
    /// </remarks>
    [HttpPost("register/phone/verify")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<TokenResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<TokenResponse>>> VerifyPhoneRegistration(
        [FromBody] VerifyPhoneRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        var tokens = await _registration.VerifyPhoneRegistrationAsync(request, cancellationToken);
        return OkResponse(tokens);
    }

    /// <summary>StartEmailVerification</summary>
    /// <remarks>
    /// App registration, step 3: emails a six-digit code to the address the signed-in member wants on
    /// the account. An address owned by another live account fails with <c>email_already_exists</c> (409).
    /// Next step: VerifyEmailCode with the same address and the code.
    /// </remarks>
    [HttpPost("me/email")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(ApiResponse<RequestMemberOtpResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse<RequestMemberOtpResponse>>> StartEmailVerification(
        [FromBody] StartEmailVerificationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _registration.StartEmailVerificationAsync(
            CurrentUser.GetRequiredUserId(),
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);
        return OkResponse(result);
    }

    /// <summary>VerifyEmailCode</summary>
    /// <remarks>
    /// App registration, step 4: verifies the emailed code, sets the address on the account and marks it
    /// confirmed. Returns the updated account. Same OTP error codes as VerifyPhoneRegistration.
    /// </remarks>
    [HttpPost("me/email/verify")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(ApiResponse<AuthAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<AuthAccountDto>>> VerifyEmailCode(
        [FromBody] VerifyEmailCodeRequest request,
        CancellationToken cancellationToken)
    {
        var account = await _registration.VerifyEmailCodeAsync(
            CurrentUser.GetRequiredUserId(),
            request,
            cancellationToken);
        return OkResponse(account);
    }

    /// <summary>SaveProfile</summary>
    /// <remarks>
    /// Writes the authenticated member's basic details, creating the profile row on the first save
    /// (the app's registration path arrives here with a phone-verified account and no profile yet).
    /// A full replace, not a patch: omitted optional fields are cleared.
    /// Required: name, gender (Male|Female|ThirdGender), dateOfBirth (18+), city.
    /// Optional: nickname (2-100 characters), heightCm, hometown, work, religion.
    /// <c>name</c> is the member's own name — a first name or a full name, their choice.
    /// <c>nickname</c> is what strangers see before a mutual match; omitted means the first letter of <c>name</c>.
    /// <c>city</c> must be an active city in the shared city catalog (<c>GET /early-access/cities/GetAll</c>);
    /// the canonical catalog name and its <c>cityId</c> are stored. Unknown or closed cities fail with <c>city_not_supported</c>.
    /// Returns the updated account with its profile.
    /// </remarks>
    [HttpPut("me/profile")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(ApiResponse<AuthAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<AuthAccountDto>>> SaveProfile(
        [FromBody] UpdateMemberProfileRequest request,
        CancellationToken cancellationToken)
    {
        var account = await _registration.SaveProfileAsync(
            CurrentUser.GetRequiredUserId(),
            request,
            cancellationToken);
        return OkResponse(account);
    }

    /// <summary>GetRegistrationProgress</summary>
    /// <remarks>
    /// Where the authenticated member stands in registration: the answers collected so far, which
    /// steps that satisfies, and <c>nextStep</c> — the first one still outstanding.
    /// The app opens the flow at <c>nextStep</c> and prefills from <c>answers</c>, so a member who
    /// left part-way resumes instead of starting again, on any device.
    /// <c>nextStep</c> is null once the personal-details half is finished; <c>profile</c> is
    /// non-null from that point on.
    /// </remarks>
    [HttpGet("me/registration")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(ApiResponse<RegistrationProgressDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<RegistrationProgressDto>>> GetRegistration(
        CancellationToken cancellationToken)
    {
        var progress = await _draft.GetAsync(CurrentUser.GetRequiredUserId(), cancellationToken);
        return OkResponse(progress);
    }

    /// <summary>SaveRegistrationPage</summary>
    /// <remarks>
    /// Saves one registration page's answers. Every field is optional — send only what the page in
    /// front of the member collects — and the server merges it into what is already stored.
    /// A partial write, unlike <c>PUT me/profile</c>, which replaces everything.
    /// <para>
    /// The answers are held server-side and promoted to their own table the moment that table's
    /// required set is complete — the personal details to the member's profile (name, gender,
    /// dateOfBirth, city, hometown), and the matching preferences to their own row (interestedIn,
    /// minAge, track, outcome). The two are independent: finishing one does not wait for or discard
    /// the other. After a group is promoted the same call edits it directly.
    /// </para>
    /// <para>
    /// The caller never chooses between those and does not need to know which happened —
    /// <c>profile</c> and <c>preferences</c> in the response are non-null once they exist, and
    /// <c>answers</c> holds only what is still in the draft.
    /// </para>
    /// <para>
    /// <c>maxAge</c> omitted means "not sent". To move the upper end back to open — "minAge and
    /// older" — send <c>maxAgeIsOpen: true</c> instead, which cannot be combined with a
    /// <c>maxAge</c>.
    /// </para>
    /// <para>
    /// <c>lifestyle</c>, <c>beliefs</c> and <c>vibe</c> are the optional questions. They have no
    /// required set, so they are stored as they arrive rather than waiting to be promoted.
    /// <c>lifestyle</c> and <c>beliefs</c> are keyed by question —
    /// <c>{"drink":{"option":"sometimes","public":true}}</c>; <c>vibe</c> is a flat set of chip keys.
    /// Each <b>replaces</b> its whole category: send every answer on the page, and a question left
    /// out is unanswered. "Prefer not to say" is stored as an answer and has no visibility setting.
    /// </para>
    /// <para>
    /// <c>public</c> defaults to true and is per answer: answering is not the same as publishing.
    /// Question and option keys are stored as sent — with the question list living in the app, the
    /// server checks their shape but cannot check that they name a real question.
    /// </para>
    /// <para>
    /// <c>prompts</c> is the member's full list of chosen conversation prompts, in order —
    /// <c>[{"promptId":"know","text":"…"}]</c>, at most 3 — and <b>replaces</b> what is stored.
    /// <c>text</c> is optional because a prompt can be answered by recording instead
    /// (<c>POST voice-answers/Upload</c>); a recording for a prompt dropped from the list is removed.
    /// The <c>voice</c> step is done once two prompts are answered, typed or spoken.
    /// <c>dealbreaker</c> (free text, empty clears) and <c>rhythm</c> (<c>{"socialEnergy":"…"}</c>,
    /// replaces) come from the profile editor and are not registration steps.
    /// </para>
    /// <para>
    /// <c>notificationsOn</c> is the notifications page — true or false; either completes the
    /// <c>notifications</c> step and sets every notification switch in <c>me/settings</c>.
    /// The intro video is optional and not a step.
    /// </para>
    /// Values are validated as strictly as they would be on the profile: a malformed date or an
    /// over-long name is refused on the page that collected it. Only the presence of the required
    /// set waits. Send an empty string to clear an optional value (nickname, work); a required
    /// field cannot be blanked.
    /// </remarks>
    [HttpPatch("me/registration")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(ApiResponse<RegistrationProgressDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<RegistrationProgressDto>>> SaveRegistrationPage(
        [FromBody] UpdateRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        var progress = await _draft.PatchAsync(
            CurrentUser.GetRequiredUserId(),
            request,
            cancellationToken);
        return OkResponse(progress);
    }

    /// <summary>GetMySettings</summary>
    /// <remarks>
    /// The member's settings: notification switches (<c>notifyIntroductions</c>, <c>notifyReplies</c>,
    /// <c>notifyWeekendSurprise</c> — null until answered), <c>introductionsPaused</c> with
    /// <c>pausedAtUtc</c>, and <c>visibility</c> — which profile fields show, e.g.
    /// <c>{"gender":false,"lifestyle.drink":true}</c>. A field missing from <c>visibility</c> is shown.
    /// Returns defaults when nothing has been set.
    /// </remarks>
    [HttpGet("me/settings")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(ApiResponse<MemberSettingsDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<MemberSettingsDto>>> GetMySettings(CancellationToken cancellationToken) =>
        OkResponse(await _settings.GetAsync(CurrentUser.GetRequiredUserId(), cancellationToken));

    /// <summary>UpdateMySettings</summary>
    /// <remarks>
    /// A partial change: any field left out keeps its value, and <c>visibility</c> merges per field.
    /// Pausing records when it began; pausing again does not reset that. The gender's visibility is
    /// the same value as <c>genderIsPublic</c> on the profile, and <c>lifestyle.*</c> / <c>beliefs.*</c>
    /// are the same as each answer's <c>public</c> — either path changes the one stored choice.
    /// </remarks>
    [HttpPatch("me/settings")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(ApiResponse<MemberSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<MemberSettingsDto>>> UpdateMySettings(
        [FromBody] UpdateMemberSettingsRequest request,
        CancellationToken cancellationToken) =>
        OkResponse(await _settings.UpdateAsync(CurrentUser.GetRequiredUserId(), request, cancellationToken));

    /// <summary>Me</summary>
    /// <remarks>
    /// Returns the authenticated member account for the current access token.
    /// Requires Bearer JWT and the Member policy.
    /// </remarks>
    [HttpGet("me")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(ApiResponse<AuthAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<AuthAccountDto>>> Me(CancellationToken cancellationToken)
    {
        var account = await _authService.GetMeAsync(CurrentUser.UserId!.Value, cancellationToken);
        return OkResponse(account);
    }

    /// <summary>SetPassword</summary>
    /// <remarks>
    /// Sets a password on the authenticated member if none exists, or changes it when <c>currentPassword</c> is supplied.
    /// </remarks>
    [HttpPost("me/password")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<object?>>> SetPassword(
        [FromBody] SetMemberPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await _authService.SetPasswordAsync(CurrentUser.UserId!.Value, request, cancellationToken);
        return OkResponse();
    }

    /// <summary>Deactivate</summary>
    /// <remarks>
    /// Deactivates the authenticated account (IsActive = false).
    /// Login and re-registration are blocked until Reactivate.
    /// Refresh sessions for the user are revoked.
    /// </remarks>
    [HttpPost("me/deactivate")]
    [Authorize(Policy = AuthPolicies.Member)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object?>>> Deactivate(CancellationToken cancellationToken)
    {
        await _lifecycle.DeactivateMemberAsync(CurrentUser.UserId!.Value, cancellationToken);
        return OkResponse();
    }

    /// <summary>Reactivate</summary>
    /// <remarks>
    /// Reactivates a deactivated account (IsActive = true) using a still-valid member access token.
    /// Soft-deleted and restricted accounts cannot be reactivated this way.
    /// After the access token expires, use RequestReactivation then RecoverMember instead.
    /// Login remains blocked for inactive accounts until reactivation succeeds.
    /// </remarks>
    [HttpPost("me/reactivate")]
    [Authorize(Policy = AuthPolicies.MemberReactivation)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<object?>>> Reactivate(CancellationToken cancellationToken)
    {
        await _lifecycle.ActivateMemberAsync(CurrentUser.UserId!.Value, cancellationToken);
        return OkResponse();
    }

    /// <summary>DeleteAccount</summary>
    /// <remarks>
    /// Soft-deletes the authenticated account and related data (for example refresh sessions and photos).
    /// The same phone may Register again later as a brand-new account; old data stays inaccessible.
    /// </remarks>
    [HttpDelete("me")]
    [Authorize(Policy = AuthPolicies.MemberAccountDeletion)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteAccount(CancellationToken cancellationToken)
    {
        await _lifecycle.DeleteMemberAsync(CurrentUser.UserId!.Value, cancellationToken);
        return OkResponse();
    }

    /// <summary>RequestReactivation</summary>
    /// <remarks>
    /// Sends a reactivation OTP when a matching deactivated member exists.
    /// Always returns 200 with the same shape as ForgotPassword so callers cannot probe accounts.
    /// Identifier alone does not reactivate. Next step: RecoverMember with the same identifier and code.
    /// </remarks>
    [HttpPost("reactivate/request")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<RequestMemberOtpResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse<RequestMemberOtpResponse>>> RequestReactivation(
        [FromBody] RequestMemberReactivationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _lifecycle.RequestReactivationAsync(
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);
        return OkResponse(result);
    }

    /// <summary>RecoverMember</summary>
    /// <remarks>
    /// Verifies the reactivation OTP and sets the member active again.
    /// Does not restore revoked refresh sessions or issue tokens — sign in after recovery.
    /// Login, password reset, and identifier-only calls cannot reactivate an inactive account.
    /// </remarks>
    [HttpPost("reactivate/recover")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<object?>>> RecoverMember(
        [FromBody] RecoverMemberRequest request,
        CancellationToken cancellationToken)
    {
        await _lifecycle.RecoverMemberAsync(request, cancellationToken);
        return OkResponse();
    }

    // ----- Staff (admin panel) -----

    /// <summary>ListMembers</summary>
    /// <remarks>
    /// Lists members, newest first. Any admin. Soft-deleted members are omitted.
    /// Query: <c>page</c>, <c>pageSize</c>, optional <c>search</c> (email, phone, first/last name),
    /// optional <c>isActive</c>, optional <c>isRestricted</c>.
    /// </remarks>
    [HttpGet("GetAll")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [Tags(StaffTag)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MemberAdminDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<PagedResult<MemberAdminDto>>>> ListMembers(
        [FromQuery] MemberAdminListQuery query,
        CancellationToken cancellationToken)
    {
        var page = await _userManagement.ListMembersAsync(
            CurrentUser.GetRequiredUserId(),
            query,
            cancellationToken);
        return OkResponse(page);
    }

    /// <summary>GetMember</summary>
    /// <remarks>
    /// Returns one member: profile fields plus photo and introduction-video metadata.
    /// Image and video bytes are on Profile GetMemberPhoto and GetMemberVideo. Any admin. Soft-deleted members are omitted.
    /// </remarks>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [Tags(StaffTag)]
    [ProducesResponseType(typeof(ApiResponse<MemberAdminDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MemberAdminDetailDto>>> GetMember(
        Guid id,
        CancellationToken cancellationToken)
    {
        var member = await _userManagement.GetMemberAsync(
            CurrentUser.GetRequiredUserId(),
            id,
            cancellationToken);
        return OkResponse(member);
    }

    /// <summary>RestrictMember</summary>
    /// <remarks>
    /// Restricts a member account (blocks sign-in) and revokes refresh sessions. Super-admin only.
    /// Independent of member self-deactivate/activate. Soft-deleted members are omitted (404).
    /// </remarks>
    [HttpPost("{id:guid}/restrict")]
    [Authorize(Policy = AuthPolicies.SuperAdmin)]
    [Tags(StaffTag)]
    [ProducesResponseType(typeof(ApiResponse<MemberAdminDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MemberAdminDto>>> RestrictMember(
        Guid id,
        CancellationToken cancellationToken)
    {
        var member = await _userManagement.RestrictMemberAsync(
            CurrentUser.GetRequiredUserId(),
            id,
            cancellationToken);
        return OkResponse(member);
    }

    /// <summary>UnrestrictMember</summary>
    /// <remarks>
    /// Removes a super-admin restriction from a member. Super-admin only.
    /// Soft-deleted members are omitted (404). Already unrestricted members are returned unchanged.
    /// </remarks>
    [HttpPost("{id:guid}/unrestrict")]
    [Authorize(Policy = AuthPolicies.SuperAdmin)]
    [Tags(StaffTag)]
    [ProducesResponseType(typeof(ApiResponse<MemberAdminDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MemberAdminDto>>> UnrestrictMember(
        Guid id,
        CancellationToken cancellationToken)
    {
        var member = await _userManagement.UnrestrictMemberAsync(
            CurrentUser.GetRequiredUserId(),
            id,
            cancellationToken);
        return OkResponse(member);
    }
}
