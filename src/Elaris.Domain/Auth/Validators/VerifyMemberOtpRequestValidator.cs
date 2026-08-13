using Elaris.Domain.Auth.Requests;
using Elaris.Domain.Auth.Statics;
using Elaris.Domain.Common.Validation;
using FluentValidation;

namespace Elaris.Domain.Auth.Validators;

public sealed class VerifyMemberOtpRequestValidator : AbstractValidator<VerifyMemberOtpRequest>
{
    public VerifyMemberOtpRequestValidator()
    {
        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Phone is required.")
            .Must(RequestValidation.BeValidIndianMobile)
            .WithMessage("Enter a valid Indian mobile number.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("OTP code is required.")
            .Length(4, 10).WithMessage("OTP code length is invalid.");

        RuleFor(x => x.Audience)
            .Must(audience =>
                string.IsNullOrWhiteSpace(audience)
                || string.Equals(audience, AuthAudiences.MemberWeb, StringComparison.Ordinal)
                || string.Equals(audience, AuthAudiences.MemberMobile, StringComparison.Ordinal))
            .WithMessage("Audience must be member-web or member-mobile.")
            .When(x => !string.IsNullOrWhiteSpace(x.Audience));
    }
}
