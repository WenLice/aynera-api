using Elaris.Domain.Auth.Requests;
using Elaris.Domain.Common.Validation;
using FluentValidation;

namespace Elaris.Domain.Auth.Validators;

public sealed class RequestAdminOtpRequestValidator : AbstractValidator<RequestAdminOtpRequest>
{
    public RequestAdminOtpRequestValidator()
    {
        RuleFor(x => x.Identifier)
            .NotEmpty().WithMessage("Phone or email is required.")
            .Must(RequestValidation.BeValidLoginIdentifier)
            .WithMessage("Enter a valid Indian mobile number or email.");
    }
}
