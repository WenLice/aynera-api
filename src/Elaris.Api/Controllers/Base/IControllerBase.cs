using Elaris.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Elaris.Api.Controllers.Base;

/// <summary>
/// Contract every ElAris API controller implements via <see cref="BaseController"/>.
/// </summary>
public interface IControllerBase
{
    ActionResult<ApiResponse<T>> OkResponse<T>(T data, int statusCode = StatusCodes.Status200OK);
    ActionResult<ApiResponse<object?>> OkResponse(int statusCode = StatusCodes.Status200OK);
    ActionResult<ApiResponse<T>> FailResponse<T>(string errorCode, int statusCode = StatusCodes.Status400BadRequest);
    ActionResult<ApiResponse<object?>> FailResponse(string errorCode, int statusCode = StatusCodes.Status400BadRequest);
    ActionResult<ApiResponse<object?>> ValidationFailResponse(ModelStateDictionary modelState);
}
