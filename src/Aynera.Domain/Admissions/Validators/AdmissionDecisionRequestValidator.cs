using Aynera.Domain.Admissions.Enums;
using Aynera.Domain.Admissions.Requests;
using FluentValidation;

namespace Aynera.Domain.Admissions.Validators;

public sealed class AdmissionDecisionRequestValidator : AbstractValidator<AdmissionDecisionRequest>
{
    public AdmissionDecisionRequestValidator()
    {
        RuleFor(x => x.Decision)
            .Must(AdmissionValidation.BeKnownDecision)
            .WithMessage("Decision must be one of: StartReview, Approve, Reject, Reopen.");

        // The member sees the reason when deciding whether to resubmit, so it is mandatory.
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A reason is required when rejecting an admission.")
            .MaximumLength(500).WithMessage("Reason is too long.")
            .When(x => AdmissionValidation.TryParseDecision(x.Decision, out var decision)
                && decision == AdmissionDecision.Reject);

        RuleFor(x => x.Reason)
            .MaximumLength(500).WithMessage("Reason is too long.")
            .When(x => x.Reason is not null);

        RuleFor(x => x.ReviewNote)
            .MaximumLength(2000).WithMessage("Review note is too long.")
            .When(x => x.ReviewNote is not null);
    }
}
