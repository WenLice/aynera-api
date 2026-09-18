using Aynera.Application.Common;
using Aynera.Application.Features.Admissions.Models;
using Aynera.Application.Features.Admissions.Repositories;
using Aynera.Application.Features.Preferences.Repositories;
using Aynera.Application.Features.Profiles.Repositories;
using Aynera.Infrastructure.Authorization;
using Aynera.Application.Features.Audit.Repositories;
using Aynera.Application.Features.Auth.Models;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Application.Features.EarlyAccess.Models;
using Aynera.Application.Features.EarlyAccess.Repositories;
using Aynera.Application.Features.Feedback.Models;
using Aynera.Application.Features.Feedback.Repositories;
using Aynera.Application.Features.Media.Models;
using Aynera.Application.Features.Media.Services.Interfaces;
using Aynera.Application.Features.Photos.Models;
using Aynera.Application.Features.Photos.Repositories;
using Aynera.Application.Features.Photos.Services.Interfaces;
using Aynera.Application.Features.PublicForms.Repositories;
using Aynera.Application.Features.Suggestions.Models;
using Aynera.Application.Features.Suggestions.Repositories;
using Aynera.Application.Features.Venues.Repositories;
using Aynera.Application.Features.Videos.Models;
using Aynera.Application.Features.Videos.Repositories;
using Aynera.Application.Features.Videos.Services.Interfaces;
using Aynera.Domain.Auth.Statics;
using Aynera.Domain.Common.Interfaces;
using Aynera.Infrastructure.Repositories;
using Aynera.Infrastructure.Services;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.Text;

namespace Aynera.Infrastructure;

public sealed class InfrastructureOptions
{
    public string RedisConfiguration { get; set; } = string.Empty;
    public bool UseInMemoryOtpStore { get; set; }
    public string ConnectionString { get; set; } = string.Empty;
}

public static class DependencyInjection
{
    public static IServiceCollection AddAyneraInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<InfrastructureOptions>? configure = null)
    {
        var infra = new InfrastructureOptions
        {
            ConnectionString =
                configuration["AYNERA_DB_CONNECTION"]
                ?? configuration.GetConnectionString("Aynera")
                ?? string.Empty,
            RedisConfiguration =
                configuration["AYNERA_REDIS"]
                ?? configuration["Aynera:Redis"]
                ?? "localhost:6379"
        };
        configure?.Invoke(infra);

        services.Configure<JwtOptions>(options =>
        {
            configuration.GetSection(JwtOptions.SectionName).Bind(options);
            options.Issuer = configuration["AYNERA_JWT_ISSUER"] ?? options.Issuer;
            options.AudienceMember = configuration["AYNERA_JWT_AUDIENCE_MEMBER"] ?? options.AudienceMember;
            options.AudienceAdmin = configuration["AYNERA_JWT_AUDIENCE_ADMIN"] ?? options.AudienceAdmin;
            options.SigningKey = configuration["AYNERA_JWT_SIGNING_KEY"] ?? options.SigningKey;
        });
        services.Configure<AdminSeedOptions>(options =>
        {
            configuration.GetSection(AdminSeedOptions.SectionName).Bind(options);
            options.Email = configuration["AYNERA_ADMIN_EMAIL"] ?? options.Email;
            options.Phone = configuration["AYNERA_ADMIN_PHONE"] ?? options.Phone;
            options.Password = configuration["AYNERA_ADMIN_PASSWORD"] ?? options.Password;
        });
        services.Configure<OtpOptions>(configuration.GetSection(OtpOptions.SectionName));
        services.Configure<PhotoOptions>(configuration.GetSection(PhotoOptions.SectionName));
        services.Configure<IntroductionVideoOptions>(configuration.GetSection(IntroductionVideoOptions.SectionName));
        services.Configure<MediaAuthenticityOptions>(configuration.GetSection(MediaAuthenticityOptions.SectionName));
        services.Configure<EarlyAccessOptions>(configuration.GetSection(EarlyAccessOptions.SectionName));
        services.Configure<AdmissionOptions>(configuration.GetSection(AdmissionOptions.SectionName));
        services.Configure<FeedbackOptions>(configuration.GetSection(FeedbackOptions.SectionName));
        services.Configure<SuggestionOptions>(configuration.GetSection(SuggestionOptions.SectionName));

        services.AddAyneraPersistence(configuration, persistence =>
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
        services.AddScoped<IWorkflowTransaction, WorkflowTransaction>();
        services.AddScoped<IVerificationEmailQueue, VerificationEmailQueue>();
        services.AddScoped<VerificationEmailDispatcher>();
        services.AddHostedService<VerificationEmailHostedService>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshSessionRepository, RefreshSessionRepository>();
        services.AddScoped<IMemberProfileRepository, MemberProfileRepository>();
        services.AddScoped<IMemberPreferencesRepository, MemberPreferencesRepository>();
        services.AddScoped<IMemberPhotoRepository, MemberPhotoRepository>();
        services.AddScoped<IIntroductionVideoRepository, IntroductionVideoRepository>();
        services.AddScoped<IEarlyAccessSignupRepository, EarlyAccessSignupRepository>();
        services.AddScoped<IEarlyAccessCityRepository, EarlyAccessCityRepository>();
        services.AddScoped<IMemberAdmissionRepository, MemberAdmissionRepository>();
        services.AddScoped<IMemberConsentRepository, MemberConsentRepository>();
        services.AddScoped<IMemberIdentityEvidenceRepository, MemberIdentityEvidenceRepository>();
        services.AddScoped<IVenueRepository, VenueRepository>();
        services.AddScoped<IVenueNotificationRepository, VenueNotificationRepository>();
        services.AddScoped<VenueNotificationDispatcher>();
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
        services.AddHostedService<VenueNotificationHostedService>();

        var jwtSection = configuration.GetSection(JwtOptions.SectionName);
        var signingKey = configuration["AYNERA_JWT_SIGNING_KEY"] ?? jwtSection["SigningKey"] ?? string.Empty;
        var issuer = configuration["AYNERA_JWT_ISSUER"] ?? jwtSection["Issuer"] ?? "aynera-api";
        var audiences = new[]
        {
            configuration["AYNERA_JWT_AUDIENCE_MEMBER"] ?? jwtSection["AudienceMember"] ?? AuthAudiences.Member,
            configuration["AYNERA_JWT_AUDIENCE_ADMIN"] ?? jwtSection["AudienceAdmin"] ?? AuthAudiences.Admin
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

        services.AddAyneraAuthorization(audiences[0], audiences[1]);

        return services;
    }
}
