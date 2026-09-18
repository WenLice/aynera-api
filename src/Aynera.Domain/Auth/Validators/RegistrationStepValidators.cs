using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Common.Validation;
using FluentValidation;

namespace Aynera.Domain.Auth.Validators;

public sealed class StartPhoneRegistrationRequestValidator : AbstractValidator<StartPhoneRegistrationRequest>
{
    public StartPhoneRegistrationRequestValidator()
    {
        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Phone is required.")
            .Must(RequestValidation.BeValidIndianMobile)
            .WithMessage("Enter a valid Indian mobile number.");
    }
}

public sealed class VerifyPhoneRegistrationRequestValidator : AbstractValidator<VerifyPhoneRegistrationRequest>
{
    public VerifyPhoneRegistrationRequestValidator()
    {
        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Phone is required.")
            .Must(RequestValidation.BeValidIndianMobile)
            .WithMessage("Enter a valid Indian mobile number.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Code is required.")
            .MaximumLength(12).WithMessage("Code is too long.");
    }
}

public sealed class StartEmailVerificationRequestValidator : AbstractValidator<StartEmailVerificationRequest>
{
    public StartEmailVerificationRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email is invalid.")
            .MaximumLength(256).WithMessage("Email is too long.");
    }
}

public sealed class VerifyEmailCodeRequestValidator : AbstractValidator<VerifyEmailCodeRequest>
{
    public VerifyEmailCodeRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email is invalid.")
            .MaximumLength(256).WithMessage("Email is too long.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Code is required.")
            .MaximumLength(12).WithMessage("Code is too long.");
    }
}
