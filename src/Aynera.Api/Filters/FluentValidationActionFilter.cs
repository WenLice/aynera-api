using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Aynera.Api.Filters;

/// <summary>
/// Runs FluentValidation for action arguments and reuses the API ModelState error envelope.
/// </summary>
public sealed class FluentValidationActionFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (context.HttpContext.RequestServices.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var validationContext = new ValidationContext<object>(argument);
            var result = await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted);
            foreach (var failure in result.Errors)
            {
                context.ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }
        }

        if (!context.ModelState.IsValid)
        {
            var factory = context.HttpContext.RequestServices
                .GetRequiredService<IOptions<ApiBehaviorOptions>>()
                .Value
                .InvalidModelStateResponseFactory;

            context.Result = factory(new ActionContext(
                context.HttpContext,
                context.RouteData,
                context.ActionDescriptor,
                context.ModelState));
            return;
        }

        await next();
    }
}
