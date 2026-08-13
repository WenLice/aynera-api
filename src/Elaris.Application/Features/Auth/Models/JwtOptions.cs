using Elaris.Domain.Auth.Statics;

namespace Elaris.Application.Features.Auth.Models;

public sealed class JwtOptions
{
    public const string SectionName = "Elaris:Jwt";

    public string Issuer { get; set; } = "elaris-api";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenLifetimeMinutes { get; set; } = 15;
    public string AudienceMemberWeb { get; set; } = AuthAudiences.MemberWeb;
    public string AudienceMemberMobile { get; set; } = AuthAudiences.MemberMobile;
    public string AudienceStaff { get; set; } = AuthAudiences.Staff;
    public int MemberWebRefreshDays { get; set; } = 30;
    public int MemberMobileRefreshDays { get; set; } = 90;
    public int StaffRefreshDays { get; set; } = 7;

    public IReadOnlyList<string> AllAudiences =>
    [
        AudienceMemberWeb,
        AudienceMemberMobile,
        AudienceStaff
    ];

    public int RefreshDaysForAudience(string audience)
    {
        if (string.Equals(audience, AudienceMemberMobile, StringComparison.Ordinal))
        {
            return MemberMobileRefreshDays;
        }

        if (string.Equals(audience, AudienceStaff, StringComparison.Ordinal))
        {
            return StaffRefreshDays;
        }

        return MemberWebRefreshDays;
    }
}
