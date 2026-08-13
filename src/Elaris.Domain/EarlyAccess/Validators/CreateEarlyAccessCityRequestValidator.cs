using Elaris.Domain.EarlyAccess.Requests;
using FluentValidation;

namespace Elaris.Domain.EarlyAccess.Validators;

public sealed class CreateEarlyAccessCityRequestValidator : AbstractValidator<CreateEarlyAccessCityRequest>
{
    public CreateEarlyAccessCityRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("City name is required.")
            .MaximumLength(100).WithMessage("City name is too long.");

        RuleFor(x => x.Wave)
            .InclusiveBetween(1, 100).WithMessage("Wave must be between 1 and 100.");
    }
}
