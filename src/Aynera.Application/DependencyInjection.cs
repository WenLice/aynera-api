using Aynera.Application.Features.Admissions.Services.Implementations;
using Aynera.Application.Features.Admissions.Services.Interfaces;
using Aynera.Application.Features.Users.Services.Implementations;
using Aynera.Application.Features.Users.Services.Interfaces;
using Aynera.Application.Features.Audit.Services.Implementations;
using Aynera.Application.Features.Audit.Services.Interfaces;
using Aynera.Application.Features.Auth.Services.Implementations;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Application.Features.EarlyAccess.Services.Implementations;
using Aynera.Application.Features.EarlyAccess.Services.Interfaces;
using Aynera.Application.Features.Feedback.Services.Implementations;
using Aynera.Application.Features.Feedback.Services.Interfaces;
using Aynera.Application.Features.Photos.Services.Implementations;
using Aynera.Application.Features.Photos.Services.Interfaces;
using Aynera.Application.Features.Suggestions.Services.Implementations;
using Aynera.Application.Features.Suggestions.Services.Interfaces;
using Aynera.Application.Features.Venues.Services.Implementations;
using Aynera.Application.Features.Venues.Services.Interfaces;
using Aynera.Application.Features.Videos.Services.Implementations;
using Aynera.Application.Features.Videos.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Aynera.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAyneraApplication(this IServiceCollection services)
    {
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IAccountLifecycleService, AccountLifecycleService>();
        services.AddScoped<IRegistrationService, RegistrationService>();
        services.AddScoped<AuthService>();
        services.AddScoped<IAuthService>(sp => sp.GetRequiredService<AuthService>());
        services.AddScoped<IAdminAuthService>(sp => sp.GetRequiredService<AuthService>());
        services.AddScoped<IPhotoService, PhotoService>();
        services.AddScoped<IIntroductionVideoService, IntroductionVideoService>();
        services.AddScoped<IEarlyAccessService, EarlyAccessService>();
        services.AddScoped<IEarlyAccessCityService, EarlyAccessCityService>();
        services.AddScoped<IVenueService, VenueService>();
        services.AddScoped<IMemberEligibilityEvaluator, MemberEligibilityEvaluator>();
        services.AddScoped<IMemberAdmissionService, MemberAdmissionService>();
        services.AddScoped<IFeedbackService, FeedbackService>();
        services.AddScoped<ISuggestionService, SuggestionService>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IAuditLogWriter, AuditLogWriter>();
        services.AddScoped<IAuditAdminService, AuditAdminService>();
        services.AddSingleton<ISpeechGuidelineService, BannedWordsGuidelineService>();
        return services;
    }
}
