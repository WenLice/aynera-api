using Elaris.Application.Features.Auth.Services.Interfaces;

namespace Elaris.Api.IntegrationTests;

/// <summary>Test double that records the last verification email.</summary>
public sealed class CapturingEmailService : IEmailService
{
    private readonly object _gate = new();
    private string? _lastEmail;
    private string? _lastVerifyUrl;

    public string? LastEmail
    {
        get
        {
            lock (_gate)
            {
                return _lastEmail;
            }
        }
    }

    public string? LastVerifyUrl
    {
        get
        {
            lock (_gate)
            {
                return _lastVerifyUrl;
            }
        }
    }

    public Task SendVerificationLinkAsync(string email, string verifyUrl, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _lastEmail = email;
            _lastVerifyUrl = verifyUrl;
        }

        return Task.CompletedTask;
    }
}
