using Elaris.Application.Features.Auth.Models;
using Elaris.Application.Features.Auth.Services.Interfaces;
using Elaris.Notifications.Email;
using Elaris.Notifications.Sms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elaris.Notifications;

public static class DependencyInjection
{
    /// <summary>
    /// Registers email/SMS adapters that implement Application contracts
    /// (<see cref="IEmailService"/>, <see cref="ISmsService"/>).
    /// </summary>
    public static IServiceCollection AddElarisNotifications(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<EmailOptions>(options =>
        {
            configuration.GetSection(EmailOptions.SectionName).Bind(options);
            options.VerifyLinkBaseUrl =
                configuration["ELARIS_EMAIL_VERIFY_LINK_BASE_URL"] ?? options.VerifyLinkBaseUrl;
            options.Provider = configuration["ELARIS_EMAIL_PROVIDER"] ?? options.Provider;
            options.SmtpHost = configuration["ELARIS_SMTP_HOST"] ?? options.SmtpHost;
            if (int.TryParse(configuration["ELARIS_SMTP_PORT"], out var smtpPort))
            {
                options.SmtpPort = smtpPort;
            }

            if (bool.TryParse(configuration["ELARIS_SMTP_USE_SSL"], out var smtpSsl))
            {
                options.SmtpUseSsl = smtpSsl;
            }

            options.SmtpUsername = configuration["ELARIS_SMTP_USERNAME"] ?? options.SmtpUsername;
            options.SmtpPassword = configuration["ELARIS_SMTP_PASSWORD"] ?? options.SmtpPassword;
            options.FromAddress = configuration["ELARIS_SMTP_FROM"] ?? options.FromAddress;
            options.FromDisplayName = configuration["ELARIS_SMTP_FROM_NAME"] ?? options.FromDisplayName;
        });

        services.Configure<SmsOptions>(options =>
        {
            configuration.GetSection(SmsOptions.SectionName).Bind(options);
            options.Provider = configuration["ELARIS_SMS_PROVIDER"] ?? options.Provider;
            options.TextbeltApiKey = configuration["ELARIS_TEXTBELT_API_KEY"] ?? options.TextbeltApiKey;
            options.TextbeltApiUrl = configuration["ELARIS_TEXTBELT_API_URL"] ?? options.TextbeltApiUrl;
        });

        var emailSection = configuration.GetSection(EmailOptions.SectionName);
        var emailProvider =
            configuration["ELARIS_EMAIL_PROVIDER"]
            ?? emailSection["Provider"]
            ?? "Console";
        var smtpHost = configuration["ELARIS_SMTP_HOST"] ?? emailSection["SmtpHost"];
        var useSmtp =
            emailProvider.Equals("Smtp", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(smtpHost);

        if (useSmtp)
        {
            services.AddSingleton<IEmailService, SmtpEmailService>();
        }
        else
        {
            services.AddSingleton<IEmailService, ConsoleEmailService>();
        }

        var smsSection = configuration.GetSection(SmsOptions.SectionName);
        var smsProvider =
            configuration["ELARIS_SMS_PROVIDER"]
            ?? smsSection["Provider"]
            ?? "Console";

        if (smsProvider.Equals("Textbelt", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<ISmsService, TextbeltSmsService>();
        }
        else
        {
            services.AddSingleton<ISmsService, ConsoleSmsService>();
        }

        return services;
    }
}
