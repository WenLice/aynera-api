using Aynera.Domain.Answers.Records;
using Aynera.Domain.Answers.Statics;
using Aynera.Domain.Auth.Statics;
using Aynera.Domain.Common.Validation;
using Aynera.Domain.Preferences.Validators;
using Aynera.Domain.Registration.Requests;
using FluentValidation;

namespace Aynera.Domain.Registration.Validators;

/// <summary>
/// Validates a single page's answers. Every rule is guarded by <c>When(... is not null)</c>, so the
/// request is judged on what it carries and never on what it omits — the same rules as
/// <c>UpdateMemberProfileRequestValidator</c>, minus the presence checks.
/// <para>
/// This is what keeps a bad date or an over-long name from being deferred to the end of
/// registration and handed back to the member as a pile.
/// </para>
/// </summary>
public sealed class UpdateRegistrationRequestValidator : AbstractValidator<UpdateRegistrationRequest>
{
    public UpdateRegistrationRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => !x.IsEmpty)
            .WithMessage("Send at least one answer.")
            .OverridePropertyName(string.Empty);

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(150).WithMessage("Name is too long.")
            .When(x => x.Name is not null);

        RuleFor(x => x.Nickname)
            .MinimumLength(2).WithMessage("A nickname needs at least 2 characters.")
            .MaximumLength(100).WithMessage("Nickname is too long.")
            .When(x => !string.IsNullOrWhiteSpace(x.Nickname));

        RuleFor(x => x.Gender)
            .IsInEnum().WithMessage("Gender is invalid.")
            .When(x => x.Gender is not null);

        RuleFor(x => x.DateOfBirth)
            .Must(dob => dob != default).WithMessage("Date of birth is required.")
            .Must(dob => dob!.Value.AddYears(AgeRules.MinimumAgeYears) <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage($"You must be at least {AgeRules.MinimumAgeYears} years old to join.")
            .When(x => x.DateOfBirth is not null);

        RuleFor(x => x.HeightCm)
            .InclusiveBetween(MemberProfileRules.HeightMinCm, MemberProfileRules.HeightMaxCm)
            .WithMessage($"Height must be between {MemberProfileRules.HeightMinCm} and {MemberProfileRules.HeightMaxCm} cm.")
            .When(x => x.HeightCm is not null);

        RuleFor(x => x.Hometown)
            .NotEmpty().WithMessage("Hometown is required.")
            .MaximumLength(100).WithMessage("Hometown is too long.")
            .When(x => x.Hometown is not null);

        RuleFor(x => x.City)
            .NotEmpty().WithMessage("City is required.")
            .MaximumLength(100).WithMessage("City is too long.")
            .When(x => x.City is not null);

        RuleFor(x => x.Work)
            .MaximumLength(200).WithMessage("Work is too long.")
            .When(x => !string.IsNullOrWhiteSpace(x.Work));

        RuleFor(x => x.InterestedIn)
            .IsInEnum().WithMessage("Choose who you'd like to meet.")
            .When(x => x.InterestedIn is not null);

        RuleFor(x => x.Track)
            .IsInEnum().WithMessage("Choose a track.")
            .When(x => x.Track is not null);

        RuleFor(x => x.Outcome)
            .IsInEnum().WithMessage("Choose what you'd be happy if this became.")
            .When(x => x.Outcome is not null);

        // The pair can only be judged when both halves are present. The app sends them together
        // from the intent step, and a stored pair that disagreed would put HardFilters at odds with
        // the member's own stated track — so it is still checked, just not demanded.
        RuleFor(x => x.Track)
            .Must((request, track) => track == PreferenceRules.TrackFor(request.Outcome!.Value))
            .WithMessage(request =>
                $"{request.Outcome} belongs to the {PreferenceRules.TrackFor(request.Outcome!.Value)} track.")
            .When(x => x.Track is not null
                && x.Outcome is not null
                && Enum.IsDefined(x.Track.Value)
                && Enum.IsDefined(x.Outcome.Value));

        RuleFor(x => x.MinAge)
            .InclusiveBetween(PreferenceRules.AgeMin, PreferenceRules.AgeMax)
            .WithMessage($"Ages run from {PreferenceRules.AgeMin} to {PreferenceRules.AgeMax}.")
            .When(x => x.MinAge is not null);

        RuleFor(x => x.MaxAge)
            .InclusiveBetween(PreferenceRules.AgeMin, PreferenceRules.AgeMax)
            .WithMessage($"Ages run from {PreferenceRules.AgeMin} to {PreferenceRules.AgeMax}.")
            .When(x => x.MaxAge is not null);

        // Only checkable when both ends arrive together; the service re-checks the merged pair, so
        // a page that sends one end alone cannot smuggle an inverted range past this.
        RuleFor(x => x.MaxAge)
            .GreaterThanOrEqualTo(x => x.MinAge)
            .WithMessage("The oldest age cannot be below the youngest.")
            .When(x => x.MaxAge is not null && x.MinAge is not null);

        RuleFor(x => x)
            .Must(x => x.MaxAge is null)
            .WithMessage("An open upper end cannot also carry a maximum age.")
            .OverridePropertyName(nameof(UpdateRegistrationRequest.MaxAge))
            .When(x => x.MaxAgeIsOpen == true);

        // The everyday and belief answers. With the question list living in the app, the server
        // cannot check that a key names a real question — it checks the shape, and nothing more.
        // That limit closes when the catalog moves server-side.
        RuleFor(x => x.Lifestyle!).SetValidator(new AnswerMapValidator()).When(x => x.Lifestyle is not null);
        RuleFor(x => x.Beliefs!).SetValidator(new AnswerMapValidator()).When(x => x.Beliefs is not null);

        RuleFor(x => x.Vibe!)
            .Must(vibe => vibe.Count <= AnswerRules.VibeMaxChips)
            .WithMessage($"At most {AnswerRules.VibeMaxChips} vibe chips.")
            .Must(vibe => vibe.All(chip => !string.IsNullOrWhiteSpace(chip)))
            .WithMessage("A vibe chip cannot be blank.")
            .Must(vibe => vibe.All(chip => chip.Length <= AnswerRules.KeyMaxLength))
            .WithMessage("A vibe chip key is too long.")
            // Duplicates would make "how many chips do two members share" quietly wrong, so they
            // are refused rather than silently collapsed.
            .Must(vibe => vibe.Distinct(StringComparer.Ordinal).Count() == vibe.Count)
            .WithMessage("The same vibe chip was sent twice.")
            .When(x => x.Vibe is not null);
    }
}

/// <summary>
/// Shape rules for one category of answers: sane keys, a real option, and a ceiling so a client
/// that ignores its own limits cannot write an unbounded document.
/// </summary>
public sealed class AnswerMapValidator : AbstractValidator<IReadOnlyDictionary<string, MemberAnswer>>
{
    public AnswerMapValidator()
    {
        RuleFor(x => x)
            .Must(map => map.Count <= AnswerRules.CategoryMaxAnswers)
            .WithMessage($"At most {AnswerRules.CategoryMaxAnswers} answers in one category.")
            .Must(map => map.Keys.All(key => !string.IsNullOrWhiteSpace(key)))
            .WithMessage("A question key cannot be blank.")
            .Must(map => map.Keys.All(key => key.Length <= AnswerRules.KeyMaxLength))
            .WithMessage("A question key is too long.")
            .Must(map => map.Values.All(answer =>
                answer is not null
                && !string.IsNullOrWhiteSpace(answer.Option)
                && answer.Option.Length <= AnswerRules.KeyMaxLength))
            .WithMessage("Every answer needs an option key.")
            .OverridePropertyName(string.Empty);
    }
}
