using Elaris.Domain.EarlyAccess.Requests;
using FluentValidation;

namespace Elaris.Domain.EarlyAccess.Validators;

public sealed class UpdateEarlyAccessCityRequestValidator : AbstractValidator<UpdateEarlyAccessCityRequest>
{
    public UpdateEarlyAccessCityRequestValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(100).WithMessage("City name is too long.")
            .Must(name => name is null || !string.IsNullOrWhiteSpace(name))
            .WithMessage("City name is required.")
            .When(x => x.Name is not null);

        RuleFor(x => x.Wave)
            .InclusiveBetween(1, 100).WithMessage("Wave must be between 1 and 100.")
            .When(x => x.Wave.HasValue);
    }
}
