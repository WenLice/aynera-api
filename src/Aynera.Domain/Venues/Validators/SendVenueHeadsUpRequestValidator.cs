using Aynera.Domain.Venues.Requests;
using FluentValidation;

namespace Aynera.Domain.Venues.Validators;

public sealed class SendVenueHeadsUpRequestValidator : AbstractValidator<SendVenueHeadsUpRequest>
{
    public SendVenueHeadsUpRequestValidator()
    {
        RuleFor(x => x.PartySize)
            .InclusiveBetween(1, 10_000).WithMessage("Party size must be between 1 and 10000.");

        RuleFor(x => x.VisitOn)
            .Must(date => date >= VenueValidation.TodayInIndia)
            .WithMessage("Visit date cannot be in the past.");

        RuleFor(x => x.Note)
            .MaximumLength(500).WithMessage("Note is too long.")
            .When(x => x.Note is not null);
    }
}
