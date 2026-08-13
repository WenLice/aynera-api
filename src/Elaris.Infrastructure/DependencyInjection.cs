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
            options.AudienceMemberWeb = configuration["ELARIS_JWT_AUDIENCE_MEMBER_WEB"] ?? options.AudienceMemberWeb;
            options.AudienceMemberMobile =
                configuration["ELARIS_JWT_AUDIENCE_MEMBER_MOBILE"] ?? options.AudienceMemberMobile;
            options.AudienceStaff = configuration["ELARIS_JWT_AUDIENCE_STAFF"] ?? options.AudienceStaff;
            options.SigningKey = configuration["ELARIS_JWT_SIGNING_KEY"] ?? options.SigningKey;
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
        services.AddHostedService<EarlyAccessCitySeedHostedService>();

        var jwtSection = configuration.GetSection(JwtOptions.SectionName);
        var signingKey = configuration["ELARIS_JWT_SIGNING_KEY"] ?? jwtSection["SigningKey"] ?? string.Empty;
        var issuer = configuration["ELARIS_JWT_ISSUER"] ?? jwtSection["Issuer"] ?? "elaris-api";
        var audiences = new[]
        {
            configuration["ELARIS_JWT_AUDIENCE_MEMBER_WEB"] ?? jwtSection["AudienceMemberWeb"] ?? "member-web",
            configuration["ELARIS_JWT_AUDIENCE_MEMBER_MOBILE"] ?? jwtSection["AudienceMemberMobile"] ?? "member-mobile",
            configuration["ELARIS_JWT_AUDIENCE_STAFF"] ?? jwtSection["AudienceStaff"] ?? "staff"
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
                policy.RequireAuthenticatedUser().RequireRole(AuthRoles.Member));
            options.AddPolicy("Staff", policy =>
                policy.RequireAuthenticatedUser().RequireRole(AuthRoles.Staff));
        });

        return services;
    }
}
