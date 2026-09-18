namespace Aynera.Application.Features.PublicForms.Repositories;

public interface IPublicFormRateLimiter
{
    Task<(bool Allowed, int? RetryAfterSeconds)> TryAcquireAsync(
        string bucket,
        string? clientIp,
        int maxPerHour,
        CancellationToken cancellationToken);
}
