namespace Aynera.Domain.Common;

public sealed class ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public int StatusCode { get; init; }
    public string? ErrorCode { get; init; }
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }
    public string? CorrelationId { get; init; }

    public static ApiResponse<T> Ok(T data, int statusCode = 200, string? correlationId = null) =>
        new()
        {
            Success = true,
            Data = data,
            StatusCode = statusCode,
            CorrelationId = correlationId
        };

    public static ApiResponse<T> Fail(
        string errorCode,
        int statusCode = 400,
        IReadOnlyDictionary<string, string[]>? errors = null,
        string? correlationId = null) =>
        new()
        {
            Success = false,
            StatusCode = statusCode,
            ErrorCode = errorCode,
            Errors = errors,
            CorrelationId = correlationId
        };
}

public static class ApiResponse
{
    public static ApiResponse<object?> Ok(int statusCode = 200, string? correlationId = null) =>
        ApiResponse<object?>.Ok(null, statusCode, correlationId);

    public static ApiResponse<object?> Fail(
        string errorCode,
        int statusCode = 400,
        IReadOnlyDictionary<string, string[]>? errors = null,
        string? correlationId = null) =>
        ApiResponse<object?>.Fail(errorCode, statusCode, errors, correlationId);
}
