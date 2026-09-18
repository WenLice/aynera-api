using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Common.Validation;
using FluentValidation;

namespace Aynera.Domain.Auth.Validators;

public sealed class RequestMemberOtpRequestValidator : AbstractValidator<RequestMemberOtpRequest>
{
    public RequestMemberOtpRequestValidator()
    {
        RuleFor(x => x.Identifier)
            .NotEmpty().WithMessage("Phone or email is required.")
            .Must(RequestValidation.BeValidLoginIdentifier)
            .WithMessage("Enter a valid Indian mobile number or email.");
    }
}
