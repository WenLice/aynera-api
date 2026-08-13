using System.Diagnostics;
using Elaris.Application.Features.Audit.Services.Interfaces;
using Elaris.Domain.Audit.Statics;
using Elaris.Domain.Common.Interfaces;

namespace Elaris.Api.Middleware;

/// <summary>
/// Writes one AuditLog per request (method, path, status, duration). No bodies or tokens.
/// </summary>
public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;

    public RequestLoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IAuditLogWriter auditLogs,
        ICurrentUser currentUser)
    {
        if (ShouldSkip(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            await _next(context);
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var path = context.Request.Path.HasValue ? context.Request.Path.Value! : "/";
            var status = context.Response.StatusCode;
            var level = status >= 500
                ? AuditLogLevels.Error
                : status >= 400
                    ? AuditLogLevels.Warning
                    : AuditLogLevels.Information;

            await auditLogs.WriteAsync(
                level,
                $"{context.Request.Method} {path} → {status} ({elapsedMs:0} ms)",
                category: "HTTP",
                client: AuditClients.Api,
                userId: currentUser.UserId,
                clientIp: context.Connection.RemoteIpAddress?.ToString(),
                properties: new
                {
                    method = context.Request.Method,
                    path,
                    status,
                    durationMs = Math.Round(elapsedMs, 1)
                },
                cancellationToken: context.RequestAborted);
        }
    }

    private static bool ShouldSkip(PathString path) =>
        path.StartsWithSegments("/health")
        || path.StartsWithSegments("/swagger");
}
