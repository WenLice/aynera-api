using Elaris.Domain.Auth.Requests;
using Elaris.Domain.Common.Validation;
using FluentValidation;

namespace Elaris.Domain.Auth.Validators;

public sealed class RequestMemberOtpRequestValidator : AbstractValidator<RequestMemberOtpRequest>
{
    public RequestMemberOtpRequestValidator()
    {
        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Phone is required.")
            .Must(RequestValidation.BeValidIndianMobile)
            .WithMessage("Enter a valid Indian mobile number.");
    }
}
