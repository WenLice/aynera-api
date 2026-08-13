using Microsoft.AspNetCore.Mvc.Filters;

namespace Elaris.Api.Filters;

/// <summary>Logs every controller action entry for diagnostics (Serilog / Seq).</summary>
public sealed class DiagnosticLoggingFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var controller = context.Controller.GetType().Name;
        var action = context.ActionDescriptor.DisplayName ?? context.ActionDescriptor.RouteValues["action"];
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(controller);

        logger.LogInformation(
            "Action starting {Controller}.{Action} {Method} {Path}",
            controller,
            action,
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path.Value);

        var executed = await next();

        if (executed.Exception is null)
        {
            logger.LogDebug(
                "Action completed {Controller} status {StatusCode}",
                controller,
                context.HttpContext.Response.StatusCode);
        }
    }
}
