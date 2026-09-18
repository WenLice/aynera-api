using Aynera.Domain.Admissions.Requests;
using FluentValidation;

namespace Aynera.Domain.Admissions.Validators;

public sealed class AcceptConsentRequestValidator : AbstractValidator<AcceptConsentRequest>
{
    public AcceptConsentRequestValidator()
    {
        RuleFor(x => x.PolicyKind)
            .Must(AdmissionValidation.BeKnownConsentKind)
            .WithMessage("Policy kind must be one of: Terms, Privacy, CommunityGuidelines.");

        RuleFor(x => x.Version)
            .NotEmpty().WithMessage("Policy version is required.")
            .MaximumLength(32).WithMessage("Policy version is too long.");
    }
}
