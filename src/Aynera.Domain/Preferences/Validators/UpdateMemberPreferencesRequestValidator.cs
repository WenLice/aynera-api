using Aynera.Domain.Preferences.Requests;
using FluentValidation;

namespace Aynera.Domain.Preferences.Validators;

public sealed class UpdateMemberPreferencesRequestValidator
    : AbstractValidator<UpdateMemberPreferencesRequest>
{
    public UpdateMemberPreferencesRequestValidator()
    {
        RuleFor(x => x.InterestedIn)
            .IsInEnum().WithMessage("Choose who you'd like to meet.");

        RuleFor(x => x.IntentOutcome)
            .IsInEnum().WithMessage("Choose what you'd be happy if this became.");

        RuleFor(x => x.MinAge)
            .InclusiveBetween(PreferenceRules.AgeMin, PreferenceRules.AgeMax)
            .WithMessage($"Ages run from {PreferenceRules.AgeMin} to {PreferenceRules.AgeMax}.");

        RuleFor(x => x.MaxAge)
            .InclusiveBetween(PreferenceRules.AgeMin, PreferenceRules.AgeMax)
            .WithMessage($"Ages run from {PreferenceRules.AgeMin} to {PreferenceRules.AgeMax}.");

        RuleFor(x => x.MaxAge)
            .GreaterThanOrEqualTo(x => x.MinAge)
            .WithMessage("The oldest age cannot be below the youngest.");
    }
}
