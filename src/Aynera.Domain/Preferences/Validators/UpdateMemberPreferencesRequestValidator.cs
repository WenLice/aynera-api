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

        RuleFor(x => x.Outcome)
            .IsInEnum().WithMessage("Choose what you'd be happy if this became.");

        RuleFor(x => x.Track)
            .IsInEnum().WithMessage("Choose a track.");

        // The track is stored, so it has to be checked rather than trusted: without this a row
        // could claim a Fluid member whose outcome belongs to Intent, and HardFilters would then
        // disagree with the member's own stated track.
        RuleFor(x => x.Track)
            .Must((request, track) => track == PreferenceRules.TrackFor(request.Outcome))
            .WithMessage(request =>
                $"{request.Outcome} belongs to the {PreferenceRules.TrackFor(request.Outcome)} track.")
            .When(x => Enum.IsDefined(x.Outcome) && Enum.IsDefined(x.Track));

        RuleFor(x => x.MinAge)
            .InclusiveBetween(PreferenceRules.AgeMin, PreferenceRules.AgeMax)
            .WithMessage($"Ages run from {PreferenceRules.AgeMin} to {PreferenceRules.AgeMax}.");

        // Null is an open upper end, so both rules only apply when a value was actually sent.
        RuleFor(x => x.MaxAge)
            .InclusiveBetween(PreferenceRules.AgeMin, PreferenceRules.AgeMax)
            .WithMessage($"Ages run from {PreferenceRules.AgeMin} to {PreferenceRules.AgeMax}.")
            .When(x => x.MaxAge.HasValue);

        RuleFor(x => x.MaxAge)
            .GreaterThanOrEqualTo(x => x.MinAge)
            .WithMessage("The oldest age cannot be below the youngest.")
            .When(x => x.MaxAge.HasValue);
    }
}
