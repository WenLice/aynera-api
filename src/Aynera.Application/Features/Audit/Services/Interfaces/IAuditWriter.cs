using Aynera.Domain.Audit.Records;

namespace Aynera.Application.Features.Audit.Services.Interfaces;

public interface IAuditWriter
{
    /// <summary>Writes AuditLog + AuditEvent. Failures are swallowed after logging.</summary>
    Task WriteAsync(AuditEventWriteModel model, CancellationToken cancellationToken);
}
