using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Common.Validation;
using FluentValidation;

namespace Aynera.Domain.Auth.Validators;

public sealed class VerifyAdminOtpRequestValidator : AbstractValidator<VerifyAdminOtpRequest>
{
    public VerifyAdminOtpRequestValidator()
    {
        RuleFor(x => x.Identifier)
            .NotEmpty().WithMessage("Phone or email is required.")
            .Must(RequestValidation.BeValidLoginIdentifier)
            .WithMessage("Enter a valid Indian mobile number or email.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("OTP code is required.")
            .Length(4, 10).WithMessage("OTP code length is invalid.");
    }
}
