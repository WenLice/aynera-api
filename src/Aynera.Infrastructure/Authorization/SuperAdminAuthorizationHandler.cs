using System.Security.Claims;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Domain.Auth.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Aynera.Infrastructure.Authorization;

public sealed class SuperAdminAuthorizationHandler(IUserRepository users)
    : AuthorizationHandler<SuperAdminRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SuperAdminRequirement requirement)
    {
        var subject = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub");
        if (context.User.Identity?.IsAuthenticated != true || !Guid.TryParse(subject, out var userId))
            return;

        var cancellationToken = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        var user = await users.FindByIdAsync(userId, cancellationToken);
        // Read current privileges on every request; never trust a super-admin JWT claim.
        if (user is { IsActive: true, IsDeleted: false, IsSuperAdmin: true }
            && string.Equals(user.AccountKind, nameof(AccountKind.Admin), StringComparison.Ordinal))
        {
            context.Succeed(requirement);
        }
    }
}
