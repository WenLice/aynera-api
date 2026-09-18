using Aynera.Domain.Auth.Statics;

namespace Aynera.Application.Features.Auth.Models;

public sealed class JwtOptions
{
    public const string SectionName = "Aynera:Jwt";

    public string Issuer { get; set; } = "aynera-api";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenLifetimeMinutes { get; set; } = 60;
    public string AudienceMember { get; set; } = AuthAudiences.Member;
    public string AudienceAdmin { get; set; } = AuthAudiences.Admin;
    public int MemberRefreshDays { get; set; } = 90;
    public int AdminRefreshHours { get; set; } = 24;

    public IReadOnlyList<string> AllAudiences =>
    [
        AudienceMember,
        AudienceAdmin
    ];

    public TimeSpan RefreshLifetimeForAudience(string audience)
    {
        if (string.Equals(audience, AudienceAdmin, StringComparison.Ordinal))
        {
            return TimeSpan.FromHours(AdminRefreshHours);
        }

        return TimeSpan.FromDays(MemberRefreshDays);
    }
}
