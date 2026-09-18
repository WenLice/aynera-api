namespace Aynera.Application.Common;

public interface IWorkflowTransaction
{
    Task<T> ExecuteAsync<T>(IReadOnlyList<string> lockKeys,
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);
}
