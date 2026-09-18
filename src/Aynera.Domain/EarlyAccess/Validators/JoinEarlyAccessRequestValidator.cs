using Aynera.Domain.Common;
using Aynera.Domain.Common.Validation;
using Aynera.Domain.EarlyAccess.Requests;
using FluentValidation;

namespace Aynera.Domain.EarlyAccess.Validators;

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
            .WithMessage("Interest must be Aynera or Aynera Professionals.");

        RuleFor(x => x.IsAdult)
            .Equal(true).WithMessage("You must confirm that you are 18 years or older.");

        RuleFor(x => x.MarketingConsent)
            .Equal(true).WithMessage("Marketing email consent is required to join early access.");

        RuleFor(x => x.Intent)
            .NotEmpty().WithMessage("Track is required.")
            .MaximumLength(64).WithMessage("Track is too long.")
            .Must(value => value is "Fluid" or "Intent")
            .WithMessage("Track must be Fluid or Intent.");

        RuleFor(x => x.MeetPreference)
            .NotEmpty().WithMessage("Meeting preference is required.")
            .MaximumLength(32).WithMessage("Meeting preference is too long.")
            .Must(value => value is "Duos" or "Squads" or "Both")
            .WithMessage("Meeting preference must be Duos, Squads, or Both.");
    }
}
