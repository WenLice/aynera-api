using Aynera.Domain.Common.Validation;
using Aynera.Domain.Venues.Enums;
using Aynera.Domain.Venues.Requests;
using FluentValidation;

namespace Aynera.Domain.Venues.Validators;

public sealed class UpdateVenueRequestValidator : AbstractValidator<UpdateVenueRequest>
{
    public UpdateVenueRequestValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(200).WithMessage("Venue name is too long.")
            .Must(name => name is null || !string.IsNullOrWhiteSpace(name))
            .WithMessage("Venue name is required.")
            .When(x => x.Name is not null);

        RuleFor(x => x.Type)
            .Must(VenueValidation.BeKnownVenueType!)
            .WithMessage("Venue type must be one of: Cafe, EventPlace.")
            .When(x => x.Type is not null);

        RuleFor(x => x.CityId)
            .NotEmpty().WithMessage("City is required.")
            .When(x => x.CityId.HasValue);

        RuleFor(x => x.Area)
            .MaximumLength(150).WithMessage("Area is too long.")
            .Must(area => area is null || !string.IsNullOrWhiteSpace(area))
            .WithMessage("Area is required.")
            .When(x => x.Area is not null);

        RuleFor(x => x.Address)
            .MaximumLength(500).WithMessage("Address is too long.")
            .Must(address => address is null || !string.IsNullOrWhiteSpace(address))
            .WithMessage("Address is required.")
            .When(x => x.Address is not null);

        RuleFor(x => x.ContactName)
            .MaximumLength(200).WithMessage("Venue contact name is too long.")
            .Must(name => name is null || !string.IsNullOrWhiteSpace(name))
            .WithMessage("Venue contact name is required.")
            .When(x => x.ContactName is not null);

        RuleFor(x => x.ContactEmail)
            .MaximumLength(256).WithMessage("Venue contact email is too long.")
            .Must(RequestValidation.BeValidEmail).WithMessage("Venue contact email is invalid.")
            .When(x => x.ContactEmail is not null);

        RuleFor(x => x.ContactPhoneE164)
            .MaximumLength(32).WithMessage("Venue contact phone is too long.")
            .Must(phone => phone is null || !string.IsNullOrWhiteSpace(phone))
            .WithMessage("Venue contact phone is required.")
            .When(x => x.ContactPhoneE164 is not null);

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
