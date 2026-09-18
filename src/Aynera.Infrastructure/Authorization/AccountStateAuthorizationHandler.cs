using System.Security.Claims;
using Aynera.Application.Features.Auth.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Aynera.Infrastructure.Authorization;

public sealed class AccountStateAuthorizationHandler(IUserRepository users)
    : AuthorizationHandler<AccountStateRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, AccountStateRequirement requirement)
    {
        var subject = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub");
        if (context.User.Identity?.IsAuthenticated != true || !Guid.TryParse(subject, out var userId))
            return;

        var ct = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        var user = await users.FindByIdAsync(userId, ct);
        if (user is not null && !user.IsDeleted
            && string.Equals(user.AccountKind, requirement.Kind.ToString(), StringComparison.Ordinal)
            && (requirement.AllowInactive || user.IsActive)
            && (requirement.AllowRestricted || !user.IsRestricted))
        {
            context.Succeed(requirement);
        }
    }
}
