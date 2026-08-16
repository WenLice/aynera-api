using Elaris.Application.Features.Audit.Repositories;
using Elaris.Application.Features.Auth.Models;
using Elaris.Application.Features.Auth.Repositories;
using Elaris.Application.Features.Auth.Services.Interfaces;
using Elaris.Application.Features.EarlyAccess.Models;
using Elaris.Application.Features.EarlyAccess.Repositories;
using Elaris.Application.Features.Feedback.Models;
using Elaris.Application.Features.Feedback.Repositories;
using Elaris.Application.Features.Media.Models;
using Elaris.Application.Features.Media.Services.Interfaces;
using Elaris.Application.Features.Photos.Models;
using Elaris.Application.Features.Photos.Repositories;
using Elaris.Application.Features.Photos.Services.Interfaces;
using Elaris.Application.Features.PublicForms.Repositories;
using Elaris.Application.Features.Suggestions.Models;
using Elaris.Application.Features.Suggestions.Repositories;
using Elaris.Application.Features.Videos.Models;
using Elaris.Application.Features.Videos.Repositories;
using Elaris.Application.Features.Videos.Services.Interfaces;
using Elaris.Domain.Auth.Statics;
using Elaris.Domain.Common.Interfaces;
using Elaris.Infrastructure.Repositories;
using Elaris.Infrastructure.Services;
using Elaris.Persistence;
using Elaris.Persistence.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Elaris.Infrastructure;

public sealed class InfrastructureOptions
{
    public string RedisConfiguration { get; set; } = string.Empty;
    public bool UseInMemoryOtpStore { get; set; }
    public string ConnectionString { get; set; } = string.Empty;
}

public static class DependencyInjection
{
    public static IServiceCollection AddElarisInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<InfrastructureOptions>? configure = null)
    {
        var infra = new InfrastructureOptions
        {
            ConnectionString =
                configuration["ELARIS_DB_CONNECTION"]
                ?? configuration.GetConnectionString("Elaris")
                ?? string.Empty,
            RedisConfiguration =
                configuration["ELARIS_REDIS"]
                ?? configuration["Elaris:Redis"]
                ?? "localhost:6379"
        };
        configure?.Invoke(infra);

        services.Configure<JwtOptions>(options =>
        {
            configuration.GetSection(JwtOptions.SectionName).Bind(options);
            options.Issuer = configuration["ELARIS_JWT_ISSUER"] ?? options.Issuer;
            options.AudienceMember = configuration["ELARIS_JWT_AUDIENCE_MEMBER"] ?? options.AudienceMember;
            options.AudienceAdmin = configuration["ELARIS_JWT_AUDIENCE_ADMIN"] ?? options.AudienceAdmin;
            options.SigningKey = configuration["ELARIS_JWT_SIGNING_KEY"] ?? options.SigningKey;
        });
        services.Configure<AdminSeedOptions>(options =>
        {
            configuration.GetSection(AdminSeedOptions.SectionName).Bind(options);
            options.Email = configuration["ELARIS_ADMIN_EMAIL"] ?? options.Email;
            options.Phone = configuration["ELARIS_ADMIN_PHONE"] ?? options.Phone;
            options.Password = configuration["ELARIS_ADMIN_PASSWORD"] ?? options.Password;
        });
        services.Configure<OtpOptions>(configuration.GetSection(OtpOptions.SectionName));
        services.Configure<PhotoOptions>(configuration.GetSection(PhotoOptions.SectionName));
        services.Configure<IntroductionVideoOptions>(configuration.GetSection(IntroductionVideoOptions.SectionName));
        services.Configure<MediaAuthenticityOptions>(configuration.GetSection(MediaAuthenticityOptions.SectionName));
        services.Configure<EarlyAccessOptions>(configuration.GetSection(EarlyAccessOptions.SectionName));
        services.Configure<FeedbackOptions>(configuration.GetSection(FeedbackOptions.SectionName));
        services.Configure<SuggestionOptions>(configuration.GetSection(SuggestionOptions.SectionName));

        services.AddElarisPersistence(configuration, persistence =>
        {
            persistence.ConnectionString = infra.ConnectionString;
        });

        if (infra.UseInMemoryOtpStore)
        {
            services.AddSingleton<IOtpChallengeRepository, InMemoryOtpChallengeRepository>();
            services.AddSingleton<IPublicFormRateLimiter, InMemoryPublicFormRateLimiter>();
        }
        else
        {
            services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(infra.RedisConfiguration));
            services.AddSingleton<IOtpChallengeRepository, RedisOtpChallengeRepository>();
            services.AddSingleton<IPublicFormRateLimiter, RedisPublicFormRateLimiter>();
        }

        services.AddAutoMapper(
            typeof(Application.DependencyInjection).Assembly,
            typeof(DependencyInjection).Assembly);

        services.AddScoped<CorrelationIdAccessor>();
        services.AddScoped<ICorrelationId>(sp => sp.GetRequiredService<CorrelationIdAccessor>());
        services.AddScoped<CurrentUser>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IAuditEventRepository, AuditEventRepository>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshSessionRepository, RefreshSessionRepository>();
        services.AddScoped<IMemberProfileRepository, MemberProfileRepository>();
        services.AddScoped<IMemberPhotoRepository, MemberPhotoRepository>();
        services.AddScoped<IIntroductionVideoRepository, IntroductionVideoRepository>();
        services.AddScoped<IEarlyAccessSignupRepository, EarlyAccessSignupRepository>();
        services.AddScoped<IEarlyAccessCityRepository, EarlyAccessCityRepository>();
        services.AddScoped<ISuggestionRepository, SuggestionRepository>();
        services.AddScoped<IFeedbackSubmissionRepository, FeedbackSubmissionRepository>();
        services.AddSingleton<IFaceMatchService, StubFaceMatchService>();
        services.AddSingleton<IImageProcessor, ImageSharpProcessor>();
        services.AddSingleton<IMediaAuthenticityService, HeuristicMediaAuthenticityService>();
        services.AddSingleton<IVideoFrameExtractor, StubVideoFrameExtractor>();
        services.AddSingleton<ISpeechTranscriptionService, StubSpeechTranscriptionService>();
        services.AddHostedService<RoleSeedHostedService>();
        services.AddHostedService<AdminSeedHostedService>();
        services.AddHostedService<EarlyAccessCitySeedHostedService>();

        var jwtSection = configuration.GetSection(JwtOptions.SectionName);
        var signingKey = configuration["ELARIS_JWT_SIGNING_KEY"] ?? jwtSection["SigningKey"] ?? string.Empty;
        var issuer = configuration["ELARIS_JWT_ISSUER"] ?? jwtSection["Issuer"] ?? "elaris-api";
        var audiences = new[]
        {
            configuration["ELARIS_JWT_AUDIENCE_MEMBER"] ?? jwtSection["AudienceMember"] ?? AuthAudiences.Member,
            configuration["ELARIS_JWT_AUDIENCE_ADMIN"] ?? jwtSection["AudienceAdmin"] ?? AuthAudiences.Admin
        };

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudiences = audiences,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier,
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("Member", policy =>
                policy.RequireAuthenticatedUser()
                    .RequireRole(AuthRoles.Member)
                    .RequireAssertion(ctx => HasJwtAudience(ctx.User, AuthAudiences.Member)));
            options.AddPolicy("Admin", policy =>
                policy.RequireAuthenticatedUser()
                    .RequireRole(AuthRoles.Admin)
                    .RequireAssertion(ctx => HasJwtAudience(ctx.User, AuthAudiences.Admin)));
        });

        return services;
    }

    private static bool HasJwtAudience(ClaimsPrincipal user, string audience)
    {
        foreach (var claim in user.Claims)
        {
            if ((claim.Type is JwtRegisteredClaimNames.Aud or "aud"
                    or "http://schemas.microsoft.com/identity/claims/audience")
                && string.Equals(claim.Value, audience, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
