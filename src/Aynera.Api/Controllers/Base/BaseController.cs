using Aynera.Api.Middleware;
using Aynera.Domain.Common;
using Aynera.Domain.Common.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Aynera.Api.Controllers.Base;

[ApiController]
[Produces("application/json")]
[ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status500InternalServerError)]
public abstract class BaseController : ControllerBase, IControllerBase
{
    protected ICurrentUser CurrentUser =>
        HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

    private string? CorrelationId =>
        HttpContext.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var value)
            ? value?.ToString()
            : null;

    [NonAction]
    public ActionResult<ApiResponse<T>> OkResponse<T>(T data, int statusCode = StatusCodes.Status200OK) =>
        StatusCode(statusCode, ApiResponse<T>.Ok(data, statusCode, CorrelationId));

    [NonAction]
    public ActionResult<ApiResponse<object?>> OkResponse(int statusCode = StatusCodes.Status200OK) =>
        StatusCode(statusCode, ApiResponse.Ok(statusCode, CorrelationId));

    [NonAction]
    public ActionResult<ApiResponse<T>> FailResponse<T>(
        string errorCode,
        int statusCode = StatusCodes.Status400BadRequest) =>
        StatusCode(statusCode, ApiResponse<T>.Fail(errorCode, statusCode, correlationId: CorrelationId));

    [NonAction]
    public ActionResult<ApiResponse<object?>> FailResponse(
        string errorCode,
        int statusCode = StatusCodes.Status400BadRequest) =>
        StatusCode(statusCode, ApiResponse.Fail(errorCode, statusCode, correlationId: CorrelationId));

    [NonAction]
    public ActionResult<ApiResponse<object?>> ValidationFailResponse(ModelStateDictionary modelState)
    {
        var errors = modelState
            .Where(pair => pair.Value is { Errors.Count: > 0 })
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value!.Errors
                    .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage)
                    .ToArray());

        const int statusCode = StatusCodes.Status400BadRequest;
        return StatusCode(statusCode, ApiResponse.Fail(
            "validation_failed",
            statusCode,
            errors,
            CorrelationId));
    }
}