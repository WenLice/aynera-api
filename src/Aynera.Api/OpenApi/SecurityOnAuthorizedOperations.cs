using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Aynera.Api.OpenApi;

/// <summary>
/// Puts the Bearer requirement only on operations that actually demand a token.
///
/// A document-level requirement applies to every operation, so anonymous endpoints such as
/// <c>GET early-access/cities/GetAll</c> and <c>POST early-access/register</c> showed a padlock
/// and read as restricted — the public marketing site calls both without any token at all.
/// Deriving this from the same metadata the pipeline enforces means the padlock cannot drift
/// from what the endpoint really does.
/// </summary>
public sealed class SecurityOnAuthorizedOperations : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;

        // [AllowAnonymous] wins over any [Authorize] in scope, exactly as it does at runtime.
        if (metadata.OfType<IAllowAnonymous>().Any()
            || !metadata.OfType<IAuthorizeData>().Any())
        {
            operation.Security = null;
            return;
        }

        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer")] = []
            }
        ];
    }
}
