using Elaris.Domain.Audit.Records;

namespace Elaris.Application.Features.Audit.Services.Interfaces;

public interface IAuditLogWriter
{
    Task WriteAsync(
        string level,
        string message,
        string? category,
        string client,
        Guid? userId,
        string? clientIp,
        object? properties,
        CancellationToken cancellationToken);
}
