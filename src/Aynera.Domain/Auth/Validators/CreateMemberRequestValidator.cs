using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Statics;
using Aynera.Domain.Common.Validation;
using FluentValidation;

namespace Aynera.Domain.Auth.Validators;

public sealed class CreateMemberRequestValidator : AbstractValidator<CreateMemberRequest>
{
    public CreateMemberRequestValidator()
    {
        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Phone is required.")
            .Must(RequestValidation.BeValidIndianMobile)
            .WithMessage("Enter a valid Indian mobile number.");

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100).WithMessage("First name is too long.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(100).WithMessage("Last name is too long.");

        RuleFor(x => x.Gender)
            .IsInEnum().WithMessage("Gender is invalid.");

        RuleFor(x => x.DateOfBirth)
            .Must(dob => dob != default).WithMessage("Date of birth is required.")
            .Must(dob => dob <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Date of birth cannot be in the future.");

        RuleFor(x => x.City)
            .NotEmpty().WithMessage("City is required.")
            .MaximumLength(100).WithMessage("City is too long.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email is invalid.")
            .MaximumLength(256).WithMessage("Email is too long.");

        RuleFor(x => x.Religion)
            .MaximumLength(100).WithMessage("Religion is too long.")
            .When(x => !string.IsNullOrWhiteSpace(x.Religion));

        RuleFor(x => x.Password)
            .Must(PasswordRules.IsWellFormed)
            .WithMessage("Password must be at least 8 characters and include a lowercase letter and a number.")
            .When(x => !string.IsNullOrWhiteSpace(x.Password));
    }
}
