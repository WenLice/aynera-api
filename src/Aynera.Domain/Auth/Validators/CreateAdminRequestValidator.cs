using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Statics;
using Aynera.Domain.Common.Validation;
using FluentValidation;

namespace Aynera.Domain.Auth.Validators;

public sealed class CreateAdminRequestValidator : AbstractValidator<CreateAdminRequest>
{
    public CreateAdminRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .Must(RequestValidation.BeValidEmail)
            .WithMessage("Email is invalid.")
            .MaximumLength(256).WithMessage("Email is too long.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .Must(PasswordRules.IsWellFormed)
            .WithMessage("Password must be at least 8 characters and include a lowercase letter and a number.");

        RuleFor(x => x.Phone)
            .Must(RequestValidation.BeValidIndianMobile)
            .WithMessage("Enter a valid Indian mobile number.")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));
    }
}
