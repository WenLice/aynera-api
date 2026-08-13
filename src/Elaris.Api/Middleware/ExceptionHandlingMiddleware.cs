using System.Text.Json;
using Elaris.Api.Middleware;
using Elaris.Domain.Auth.Exceptions;
using Elaris.Domain.Common;
using Elaris.Domain.EarlyAccess.Exceptions;
using Elaris.Domain.Feedback.Exceptions;
using Elaris.Domain.Photos.Exceptions;
using Elaris.Domain.Suggestions.Exceptions;
using Elaris.Domain.Videos.Exceptions;

namespace Elaris.Api.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AuthException ex)
        {
            _logger.LogWarning(ex, "Auth failure {ErrorCode}", ex.ErrorCode);
            await WriteApiResponseAsync(context, ex.StatusCode, ex.ErrorCode);
        }
        catch (PhotoException ex)
        {
            _logger.LogWarning(ex, "Photo failure {ErrorCode}", ex.ErrorCode);
            await WriteApiResponseAsync(context, ex.StatusCode, ex.ErrorCode);
        }
        catch (VideoException ex)
        {
            _logger.LogWarning(ex, "Video failure {ErrorCode}", ex.ErrorCode);
            await WriteApiResponseAsync(context, ex.StatusCode, ex.ErrorCode);
        }
        catch (EarlyAccessException ex)
        {
            _logger.LogWarning(ex, "Early access failure {ErrorCode}", ex.ErrorCode);
            await WriteApiResponseAsync(context, ex.StatusCode, ex.ErrorCode);
        }
        catch (FeedbackException ex)
        {
            _logger.LogWarning(ex, "Feedback failure {ErrorCode}", ex.ErrorCode);
            await WriteApiResponseAsync(context, ex.StatusCode, ex.ErrorCode);
        }
        catch (SuggestionException ex)
        {
            _logger.LogWarning(ex, "Suggestion failure {ErrorCode}", ex.ErrorCode);
            await WriteApiResponseAsync(context, ex.StatusCode, ex.ErrorCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            if (_environment.IsDevelopment())
            {
                _logger.LogDebug(ex, "Unhandled exception detail");
            }

            await WriteApiResponseAsync(context, StatusCodes.Status500InternalServerError, "internal_error");
        }
    }

    private static async Task WriteApiResponseAsync(
        HttpContext context,
        int statusCode,
        string errorCode)
    {
        if (context.Response.HasStarted)
        {
            throw new InvalidOperationException("The response has already started.");
        }

        var correlationId = context.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var value)
            ? value?.ToString()
            : null;

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var payload = ApiResponse.Fail(errorCode, statusCode, correlationId: correlationId);
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
