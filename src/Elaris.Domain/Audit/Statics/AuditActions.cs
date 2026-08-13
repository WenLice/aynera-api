namespace Elaris.Domain.Audit.Statics;

public static class AuditActions
{
    public const string MemberRegistered = "member_registered";
    public const string EmailConfirmed = "email_confirmed";
    public const string OtpRequested = "otp_requested";
    public const string OtpRequestFailed = "otp_request_failed";
    public const string OtpVerified = "otp_verified";
    public const string OtpFailed = "otp_failed";
    public const string TokenRefreshed = "token_refreshed";
    public const string TokenRefreshFailed = "token_refresh_failed";
    public const string Logout = "logout";
    public const string MemberDeactivated = "member_deactivated";
    public const string MemberActivated = "member_activated";
    public const string MemberDeleted = "member_deleted";
    public const string EarlyAccessCityCreated = "early_access_city_created";
    public const string EarlyAccessCityUpdated = "early_access_city_updated";
    public const string EarlyAccessCityDeleted = "early_access_city_deleted";
    public const string EarlyAccessJoined = "early_access_joined";
    public const string PhotosUploaded = "photos_uploaded";
    public const string PhotoDeleted = "photo_deleted";
    public const string IntroVideoUploaded = "intro_video_uploaded";
    public const string IntroVideoDeleted = "intro_video_deleted";
}

public static class AuditSubjectTypes
{
    public const string User = "user";
    public const string EarlyAccessCity = "early_access_city";
    public const string EarlyAccessSignup = "early_access_signup";
    public const string MemberPhoto = "member_photo";
    public const string IntroductionVideo = "introduction_video";
}

public static class AuditClients
{
    public const string Api = "api";
    public const string MemberWeb = "member-web";
    public const string MemberMobile = "member-mobile";
    public const string Staff = "staff";
}

public static class AuditLogLevels
{
    public const string Information = "Information";
    public const string Warning = "Warning";
    public const string Error = "Error";
}
