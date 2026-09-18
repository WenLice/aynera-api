using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Statics;
using FluentValidation;

namespace Aynera.Domain.Auth.Validators;

public sealed class SetMemberPasswordRequestValidator : AbstractValidator<SetMemberPasswordRequest>
{
    public SetMemberPasswordRequestValidator()
    {
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .Must(PasswordRules.IsWellFormed)
            .WithMessage("Password must be at least 8 characters and include a lowercase letter and a number.");
    }
}
