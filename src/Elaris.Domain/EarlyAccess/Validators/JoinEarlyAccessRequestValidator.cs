using Elaris.Domain.Common;
using Elaris.Domain.Common.Validation;
using Elaris.Domain.EarlyAccess.Requests;
using FluentValidation;

namespace Elaris.Domain.EarlyAccess.Validators;

public sealed class JoinEarlyAccessRequestValidator : AbstractValidator<JoinEarlyAccessRequest>
{
    public JoinEarlyAccessRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required.")
            .MaximumLength(PublicFormLimits.MaxFullNameLength).WithMessage("Full name is too long.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email is invalid.")
            .MaximumLength(PublicFormLimits.MaxEmailLength).WithMessage("Email is too long.");

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Phone number is required.")
            .MaximumLength(32).WithMessage("Phone is too long.");

        RuleFor(x => x.City)
            .NotEmpty().WithMessage("City is required.")
            .MaximumLength(100).WithMessage("City is too long.");

        RuleFor(x => x.Interest)
            .NotEmpty().WithMessage("Interest is required.")
            .MaximumLength(64).WithMessage("Interest is too long.")
            .Must(RequestValidation.BeKnownEarlyAccessInterest)
            .WithMessage("Interest must be Elaris or Elaris Professionals.");

        RuleFor(x => x.IsAdult)
            .Equal(true).WithMessage("You must confirm that you are 18 years or older.");

        RuleFor(x => x.MarketingConsent)
            .Equal(true).WithMessage("Marketing email consent is required to join early access.");
    }
}
