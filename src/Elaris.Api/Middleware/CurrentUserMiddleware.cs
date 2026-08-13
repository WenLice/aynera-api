using System.Security.Claims;
using Elaris.Infrastructure.Services;

namespace Elaris.Api.Middleware;

/// <summary>
/// After authentication, copies the JWT subject into the request-scoped <see cref="CurrentUser"/>.
/// </summary>
public sealed class CurrentUserMiddleware
{
    private readonly RequestDelegate _next;

    public CurrentUserMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, CurrentUser currentUser)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var raw = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.User.FindFirstValue("sub");

            if (Guid.TryParse(raw, out var userId))
            {
                currentUser.SetUserId(userId);
            }
        }

        await _next(context);
    }
}
