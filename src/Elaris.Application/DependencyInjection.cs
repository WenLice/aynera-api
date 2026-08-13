using Elaris.Application.Features.Audit.Services.Implementations;
using Elaris.Application.Features.Audit.Services.Interfaces;
using Elaris.Application.Features.Auth.Services.Implementations;
using Elaris.Application.Features.Auth.Services.Interfaces;
using Elaris.Application.Features.EarlyAccess.Services.Implementations;
using Elaris.Application.Features.EarlyAccess.Services.Interfaces;
using Elaris.Application.Features.Feedback.Services.Implementations;
using Elaris.Application.Features.Feedback.Services.Interfaces;
using Elaris.Application.Features.Photos.Services.Implementations;
using Elaris.Application.Features.Photos.Services.Interfaces;
using Elaris.Application.Features.Suggestions.Services.Implementations;
using Elaris.Application.Features.Suggestions.Services.Interfaces;
using Elaris.Application.Features.Videos.Services.Implementations;
using Elaris.Application.Features.Videos.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Elaris.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddElarisApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IPhotoService, PhotoService>();
        services.AddScoped<IIntroductionVideoService, IntroductionVideoService>();
        services.AddScoped<IEarlyAccessService, EarlyAccessService>();
        services.AddScoped<IEarlyAccessCityService, EarlyAccessCityService>();
        services.AddScoped<IFeedbackService, FeedbackService>();
        services.AddScoped<ISuggestionService, SuggestionService>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IAuditLogWriter, AuditLogWriter>();
        services.AddSingleton<ISpeechGuidelineService, BannedWordsGuidelineService>();
        return services;
    }
}
