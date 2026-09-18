using Aynera.Domain.Common;
using Aynera.Domain.Feedback.Requests;
using FluentValidation;

namespace Aynera.Domain.Feedback.Validators;

public sealed class SubmitFeedbackRequestValidator : AbstractValidator<SubmitFeedbackRequest>
{
    public SubmitFeedbackRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required.")
            .MaximumLength(PublicFormLimits.MaxFullNameLength).WithMessage("Full name is too long.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email is invalid.")
            .MaximumLength(PublicFormLimits.MaxEmailLength).WithMessage("Email is too long.");

        RuleFor(x => x.Message)
            .NotEmpty().WithMessage("Message is required.")
            .MaximumLength(PublicFormLimits.MaxMessageLength)
            .WithMessage($"Message must be at most {PublicFormLimits.MaxMessageLength} characters.");

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Phone number is required.")
            .MaximumLength(32).WithMessage("Phone is too long.");
    }
}
