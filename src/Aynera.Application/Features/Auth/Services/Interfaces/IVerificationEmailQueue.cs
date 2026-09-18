namespace Aynera.Application.Features.Auth.Services.Interfaces;

public interface IVerificationEmailQueue
{
    Task CancelAsync(Guid userId, CancellationToken cancellationToken);

    Task EnqueueAsync(Guid userId, CancellationToken cancellationToken);
}
