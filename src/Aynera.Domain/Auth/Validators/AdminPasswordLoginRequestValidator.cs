using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Common.Validation;
using FluentValidation;

namespace Aynera.Domain.Auth.Validators;

public sealed class AdminPasswordLoginRequestValidator : AbstractValidator<AdminPasswordLoginRequest>
{
    public AdminPasswordLoginRequestValidator()
    {
        RuleFor(x => x.Identifier)
            .NotEmpty().WithMessage("Phone or email is required.")
            .Must(RequestValidation.BeValidLoginIdentifier)
            .WithMessage("Enter a valid Indian mobile number or email.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.");
    }
}
