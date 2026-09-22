using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Statics;
using Aynera.Domain.Common.Validation;
using FluentValidation;

namespace Aynera.Domain.Auth.Validators;

public sealed class UpdateMemberProfileRequestValidator : AbstractValidator<UpdateMemberProfileRequest>
{
    public UpdateMemberProfileRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(150).WithMessage("Name is too long.");

        RuleFor(x => x.Nickname)
            .MinimumLength(2).WithMessage("A nickname needs at least 2 characters.")
            .MaximumLength(100).WithMessage("Nickname is too long.")
            .When(x => !string.IsNullOrWhiteSpace(x.Nickname));

        RuleFor(x => x.Gender)
            .IsInEnum().WithMessage("Gender is invalid.");

        RuleFor(x => x.DateOfBirth)
            .Must(dob => dob != default).WithMessage("Date of birth is required.")
            .Must(dob => dob.AddYears(AgeRules.MinimumAgeYears) <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage($"You must be at least {AgeRules.MinimumAgeYears} years old to join.");

        RuleFor(x => x.City)
            .NotEmpty().WithMessage("City is required.")
            .MaximumLength(100).WithMessage("City is too long.");

        RuleFor(x => x.HeightCm)
            .InclusiveBetween(MemberProfileRules.HeightMinCm, MemberProfileRules.HeightMaxCm)
            .WithMessage($"Height must be between {MemberProfileRules.HeightMinCm} and {MemberProfileRules.HeightMaxCm} cm.")
            .When(x => x.HeightCm.HasValue);

        RuleFor(x => x.Hometown)
            .NotEmpty().WithMessage("Hometown is required.")
            .MaximumLength(100).WithMessage("Hometown is too long.");

        RuleFor(x => x.Work)
            .MaximumLength(200).WithMessage("Work is too long.")
            .When(x => !string.IsNullOrWhiteSpace(x.Work));

        RuleFor(x => x.Religion)
            .MaximumLength(100).WithMessage("Religion is too long.")
            .When(x => !string.IsNullOrWhiteSpace(x.Religion));
    }
}
