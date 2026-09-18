using Aynera.Domain.Auth.Requests;
using FluentValidation;

namespace Aynera.Domain.Auth.Validators;

public sealed class LogoutRequestValidator : AbstractValidator<LogoutRequest>
{
    public LogoutRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token is required.");
    }
}
