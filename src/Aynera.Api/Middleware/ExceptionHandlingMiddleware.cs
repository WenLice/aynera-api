using System.Text.Json;
using Aynera.Api.Middleware;
using Aynera.Domain.Admissions.Exceptions;
using Aynera.Domain.Auth.Exceptions;
using Aynera.Domain.Common;
using Aynera.Domain.EarlyAccess.Exceptions;
using Aynera.Domain.Venues.Exceptions;
using Aynera.Domain.Feedback.Exceptions;
using Aynera.Domain.Photos.Exceptions;
using Aynera.Domain.Suggestions.Exceptions;
using Aynera.Domain.Videos.Exceptions;

namespace Aynera.Api.Middleware;

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
        catch (VenueException ex)
        {
            _logger.LogWarning(ex, "Venue failure {ErrorCode}", ex.ErrorCode);
            await WriteApiResponseAsync(context, ex.StatusCode, ex.ErrorCode);
        }
        catch (AdmissionException ex)
        {
            _logger.LogWarning(ex, "Admission failure {ErrorCode}", ex.ErrorCode);
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
