namespace Aynera.Domain.Audit.Statics;

public static class AuditActions
{
    public const string MemberRegistered = "member_registered";
    public const string MemberProfileSaved = "member_profile_saved";
    public const string MemberPreferencesSaved = "member_preferences_saved";
    public const string EmailConfirmed = "email_confirmed";
    public const string OtpRequested = "otp_requested";
    public const string OtpRequestFailed = "otp_request_failed";
    public const string OtpVerified = "otp_verified";
    public const string OtpFailed = "otp_failed";
    public const string PasswordLoginSucceeded = "password_login_succeeded";
    public const string PasswordLoginFailed = "password_login_failed";
    public const string PasswordSet = "password_set";
    public const string PasswordResetRequested = "password_reset_requested";
    public const string PasswordResetCompleted = "password_reset_completed";
    public const string TokenRefreshed = "token_refreshed";
    public const string TokenRefreshFailed = "token_refresh_failed";
    public const string Logout = "logout";
    public const string MemberDeactivated = "member_deactivated";
    public const string MemberActivated = "member_activated";
    public const string MemberReactivationRequested = "member_reactivation_requested";
    public const string MemberRestricted = "member_restricted";
    public const string MemberUnrestricted = "member_unrestricted";
    public const string MemberDeleted = "member_deleted";
    public const string AdmissionSubmitted = "admission_submitted";
    public const string AdmissionReviewStarted = "admission_review_started";
    public const string AdmissionApproved = "admission_approved";
    public const string AdmissionRejected = "admission_rejected";
    public const string AdmissionReopened = "admission_reopened";
    public const string MemberConsentAccepted = "member_consent_accepted";
    public const string EarlyAccessCityCreated = "early_access_city_created";
    public const string EarlyAccessCityUpdated = "early_access_city_updated";
    public const string EarlyAccessCityDeleted = "early_access_city_deleted";
    public const string EarlyAccessJoined = "early_access_joined";
    public const string VenueCreated = "venue_created";
    public const string VenueUpdated = "venue_updated";
    public const string VenueDeleted = "venue_deleted";
    public const string VenueHeadsUpQueued = "venue_heads_up_queued";
    public const string PhotosUploaded = "photos_uploaded";
    public const string PhotoDeleted = "photo_deleted";
    public const string IntroVideoUploaded = "intro_video_uploaded";
    public const string IntroVideoDeleted = "intro_video_deleted";
    public const string AdminCreated = "admin_created";
    public const string AdminDeactivated = "admin_deactivated";
    public const string AdminActivated = "admin_activated";
}

public static class AuditSubjectTypes
{
    public const string User = "user";
    public const string EarlyAccessCity = "early_access_city";
    public const string MemberAdmission = "member_admission";
    public const string Venue = "venue";
    public const string EarlyAccessSignup = "early_access_signup";
    public const string MemberPhoto = "member_photo";
    public const string IntroductionVideo = "introduction_video";
}

public static class AuditClients
{
    public const string Api = "api";
    public const string Member = "member";
    public const string Admin = "admin";
}

public static class AuditLogLevels
{
    public const string Information = "Information";
    public const string Warning = "Warning";
    public const string Error = "Error";
}
