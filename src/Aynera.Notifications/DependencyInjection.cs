using Aynera.Application.Features.Auth.Models;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Notifications.Email;
using Aynera.Notifications.Sms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aynera.Notifications;

public static class DependencyInjection
{
    /// <summary>
    /// Registers email/SMS adapters that implement Application contracts
    /// (<see cref="IEmailService"/>, <see cref="ISmsService"/>).
    /// </summary>
    public static IServiceCollection AddAyneraNotifications(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<EmailOptions>(options =>
        {
            configuration.GetSection(EmailOptions.SectionName).Bind(options);
            options.VerifyLinkBaseUrl =
                configuration["AYNERA_EMAIL_VERIFY_LINK_BASE_URL"] ?? options.VerifyLinkBaseUrl;
            options.Provider = configuration["AYNERA_EMAIL_PROVIDER"] ?? options.Provider;
            options.SmtpHost = configuration["AYNERA_SMTP_HOST"] ?? options.SmtpHost;
            if (int.TryParse(configuration["AYNERA_SMTP_PORT"], out var smtpPort))
            {
                options.SmtpPort = smtpPort;
            }

            if (bool.TryParse(configuration["AYNERA_SMTP_USE_SSL"], out var smtpSsl))
            {
                options.SmtpUseSsl = smtpSsl;
            }

            options.SmtpUsername = configuration["AYNERA_SMTP_USERNAME"] ?? options.SmtpUsername;
            options.SmtpPassword = configuration["AYNERA_SMTP_PASSWORD"] ?? options.SmtpPassword;
            options.FromAddress = configuration["AYNERA_SMTP_FROM"] ?? options.FromAddress;
            options.FromDisplayName = configuration["AYNERA_SMTP_FROM_NAME"] ?? options.FromDisplayName;
        });

        services.Configure<SmsOptions>(options =>
        {
            configuration.GetSection(SmsOptions.SectionName).Bind(options);
            options.Provider = configuration["AYNERA_SMS_PROVIDER"] ?? options.Provider;
            options.TextbeltApiKey = configuration["AYNERA_TEXTBELT_API_KEY"] ?? options.TextbeltApiKey;
            options.TextbeltApiUrl = configuration["AYNERA_TEXTBELT_API_URL"] ?? options.TextbeltApiUrl;
            options.TwoFactorApiKey = configuration["AYNERA_2FACTOR_API_KEY"] ?? options.TwoFactorApiKey;
            options.TwoFactorTemplateName = configuration["AYNERA_2FACTOR_TEMPLATE"] ?? options.TwoFactorTemplateName;
            options.TwoFactorOtpUrl = configuration["AYNERA_2FACTOR_OTP_URL"] ?? options.TwoFactorOtpUrl;
        });

        var emailSection = configuration.GetSection(EmailOptions.SectionName);
        var emailProvider =
            configuration["AYNERA_EMAIL_PROVIDER"]
            ?? emailSection["Provider"]
            ?? "Console";
        var smtpHost = configuration["AYNERA_SMTP_HOST"] ?? emailSection["SmtpHost"];
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
            configuration["AYNERA_SMS_PROVIDER"]
            ?? smsSection["Provider"]
            ?? "Console";

        if (smsProvider.Equals("TwoFactor", StringComparison.OrdinalIgnoreCase)
            || smsProvider.Equals("2Factor", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<ISmsService, TwoFactorSmsService>();
        }
        else if (smsProvider.Equals("Textbelt", StringComparison.OrdinalIgnoreCase))
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
