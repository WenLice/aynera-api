using Aynera.Domain.Common.Validation;
using Aynera.Domain.Venues.Enums;
using Aynera.Domain.Venues.Requests;
using FluentValidation;

namespace Aynera.Domain.Venues.Validators;

public sealed class CreateVenueRequestValidator : AbstractValidator<CreateVenueRequest>
{
    public CreateVenueRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Venue name is required.")
            .MaximumLength(200).WithMessage("Venue name is too long.");

        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Venue type is required.")
            .Must(VenueValidation.BeKnownVenueType)
            .WithMessage("Venue type must be one of: Cafe, EventPlace.");

        RuleFor(x => x.CityId)
            .NotEmpty().WithMessage("City is required.");

        RuleFor(x => x.Area)
            .NotEmpty().WithMessage("Area is required.")
            .MaximumLength(150).WithMessage("Area is too long.");

        RuleFor(x => x.Address)
            .NotEmpty().WithMessage("Address is required.")
            .MaximumLength(500).WithMessage("Address is too long.");

        RuleFor(x => x.ContactName)
            .NotEmpty().WithMessage("Venue contact name is required.")
            .MaximumLength(200).WithMessage("Venue contact name is too long.");

        RuleFor(x => x.ContactEmail)
            .NotEmpty().WithMessage("Venue contact email is required.")
            .MaximumLength(256).WithMessage("Venue contact email is too long.")
            .Must(RequestValidation.BeValidEmail).WithMessage("Venue contact email is invalid.");

        RuleFor(x => x.ContactPhoneE164)
            .NotEmpty().WithMessage("Venue contact phone is required.")
            .MaximumLength(32).WithMessage("Venue contact phone is too long.");

        RuleFor(x => x.Capacity)
            .InclusiveBetween(1, 100_000).WithMessage("Capacity must be between 1 and 100000.")
            .When(x => x.Capacity.HasValue);

        RuleFor(x => x.Notes)
            .MaximumLength(2000).WithMessage("Notes are too long.")
            .When(x => x.Notes is not null);

        RuleForEach(x => x.PhotoUrls)
            .MaximumLength(1000).WithMessage("Photo URL is too long.")
            .When(x => x.PhotoUrls is not null);

        RuleFor(x => x.PhotoUrls!)
            .Must(urls => urls.Count <= 20).WithMessage("A venue can have at most 20 photos.")
            .When(x => x.PhotoUrls is not null);
    }
}
