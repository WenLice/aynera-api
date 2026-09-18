using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Aynera.Application.Features.Auth.Models;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Auth.Records;
using Aynera.Domain.Auth.Statics;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Aynera.Infrastructure.Services;

public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly SigningCredentials _credentials;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.SigningKey) || _options.SigningKey.Length < 32)
        {
            throw new InvalidOperationException("Aynera:Jwt:SigningKey must be at least 32 characters.");
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public AccessTokenResult CreateAccessToken(
        UserRecord user,
        string audience,
        Guid sessionId,
        string amr,
        DateTimeOffset authTimeUtc)
    {
        var jti = Guid.NewGuid().ToString("N");
        var expires = DateTimeOffset.UtcNow.AddMinutes(_options.AccessTokenLifetimeMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, jti),
            new("sid", sessionId.ToString()),
            new("amr", amr),
            new("auth_time", authTimeUtc.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(ClaimTypes.NameIdentifier, user.Id.ToString())
        };

        foreach (var role in user.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
            claims.Add(new Claim("role", role));
        }

        if (string.Equals(user.AccountKind, nameof(AccountKind.Admin), StringComparison.Ordinal))
        {
            claims.Add(new Claim("is_super_admin", user.IsSuperAdmin ? "true" : "false"));
        }

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires.UtcDateTime,
            signingCredentials: _credentials);

        var encoded = new JwtSecurityTokenHandler().WriteToken(token);
        return new AccessTokenResult(encoded, jti, expires);
    }

    public IssuedRefreshToken CreateRefreshToken()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        return new IssuedRefreshToken(
            SessionId: Guid.NewGuid(),
            FamilyId: Guid.NewGuid(),
            RefreshToken: raw,
            TokenHash: TokenHasher.Hash(raw),
            ExpiresAtUtc: DateTimeOffset.UtcNow);
    }
}
