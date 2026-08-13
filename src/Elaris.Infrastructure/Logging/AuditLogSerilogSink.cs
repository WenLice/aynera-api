using System.Text.Json;
using Elaris.Application.Features.Audit.Repositories;
using Elaris.Domain.Audit.Records;
using Elaris.Domain.Audit.Statics;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace Elaris.Infrastructure.Logging;

/// <summary>
/// Persists Warning+ Serilog events to AuditLogs for durable ops when Seq is unavailable.
/// </summary>
public sealed class AuditLogSerilogSink : ILogEventSink
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IServiceProvider _services;

    public AuditLogSerilogSink(IServiceProvider services)
    {
        _services = services;
    }

    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Level < LogEventLevel.Warning)
        {
            return;
        }

        if (logEvent.Properties.TryGetValue("SourceContext", out var source)
            && source.ToString().Contains("AuditLog", StringComparison.Ordinal))
        {
            return;
        }

        _ = PersistAsync(logEvent);
    }

    private async Task PersistAsync(LogEvent logEvent)
    {
        try
        {
            await using var scope = _services.CreateAsyncScope();
            var logs = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

            string? correlationId = null;
            if (logEvent.Properties.TryGetValue("CorrelationId", out var corr))
            {
                correlationId = UnwrapScalar(corr);
            }

            Guid? userId = null;
            if (logEvent.Properties.TryGetValue("UserId", out var userProp)
                && Guid.TryParse(UnwrapScalar(userProp), out var parsed))
            {
                userId = parsed;
            }

            var category = logEvent.Properties.TryGetValue("SourceContext", out var ctx)
                ? UnwrapScalar(ctx)
                : "Serilog";

            var level = logEvent.Level switch
            {
                LogEventLevel.Error or LogEventLevel.Fatal => AuditLogLevels.Error,
                _ => AuditLogLevels.Warning
            };

            var message = logEvent.RenderMessage();
            if (message.Length > 2000)
            {
                message = message[..2000];
            }

            var props = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var pair in logEvent.Properties)
            {
                if (pair.Key is "SourceContext" or "CorrelationId" or "UserId")
                {
                    continue;
                }

                props[pair.Key] = UnwrapScalar(pair.Value);
            }

            if (logEvent.Exception is not null)
            {
                props["exceptionType"] = logEvent.Exception.GetType().Name;
                props["exceptionMessage"] = logEvent.Exception.Message;
            }

            await logs.AddAsync(
                new AuditLogRecord(
                    Guid.NewGuid(),
                    logEvent.Timestamp.ToUniversalTime(),
                    level,
                    message,
                    category,
                    AuditClients.Api,
                    correlationId,
                    userId,
                    ClientIp: null,
                    props.Count == 0 ? null : JsonSerializer.Serialize(props, JsonOptions)),
                CancellationToken.None);
        }
        catch
        {
            // Never throw from a sink.
        }
    }

    private static string? UnwrapScalar(LogEventPropertyValue value) =>
        value switch
        {
            ScalarValue { Value: null } => null,
            ScalarValue { Value: var v } => v.ToString(),
            _ => value.ToString().Trim('"')
        };
}
