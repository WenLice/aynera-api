using Aynera.Domain.Settings.Requests;
using Aynera.Domain.Settings.Statics;
using FluentValidation;

namespace Aynera.Domain.Settings.Validators;

public sealed class UpdateMemberSettingsRequestValidator : AbstractValidator<UpdateMemberSettingsRequest>
{
    public UpdateMemberSettingsRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => !x.IsEmpty)
            .WithMessage("Send at least one setting.")
            .OverridePropertyName(string.Empty);

        RuleFor(x => x.Visibility!)
            .Must(map => map.Count <= VisibilityKeys.MaxEntries)
            .WithMessage($"At most {VisibilityKeys.MaxEntries} visibility entries.")
            .Must(map => map.Keys.All(VisibilityKeys.IsValid))
            .WithMessage("A visibility field name is invalid.")
            .When(x => x.Visibility is not null);
    }
}
