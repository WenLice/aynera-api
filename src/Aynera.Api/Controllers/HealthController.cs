using Aynera.Api.Controllers.Base;
using Aynera.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aynera.Api.Controllers;

/// <summary>
/// Health checks.
/// </summary>
[Route("health")]
[Tags("Health")]
public sealed class HealthController : BaseController
{
    /// <summary>Health</summary>
    /// <remarks>Liveness check used by local tooling and future load balancers.</remarks>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<object>> Get() =>
        OkResponse<object>(new { status = "ok" });
}
