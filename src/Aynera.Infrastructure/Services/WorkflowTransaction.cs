using Aynera.Application.Common;
using Aynera.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aynera.Infrastructure.Services;

public sealed class WorkflowTransaction(AyneraDbContext db) : IWorkflowTransaction
{
    public async Task<T> ExecuteAsync<T>(IReadOnlyList<string> lockKeys,
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Stable ordering serializes competing registrations for either contact identifier.
            foreach (var key in lockKeys.Distinct().Order(StringComparer.Ordinal))
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);

            var result = await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
