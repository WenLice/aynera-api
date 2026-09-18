using Aynera.Domain.Audit.Requests;
using Aynera.Domain.Common;
using FluentValidation;

namespace Aynera.Domain.Audit.Validators;

public sealed class AuditAdminListQueryValidator : AbstractValidator<AuditAdminListQuery>
{
    public AuditAdminListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, PagedQuery.MaxPageSize)
            .WithMessage($"Page size must be between 1 and {PagedQuery.MaxPageSize}.");
        RuleFor(x => x.Action)
            .MaximumLength(64)
            .When(x => !string.IsNullOrWhiteSpace(x.Action));
        RuleFor(x => x.SubjectType)
            .MaximumLength(64)
            .When(x => !string.IsNullOrWhiteSpace(x.SubjectType));
        RuleFor(x => x.SubjectId)
            .MaximumLength(64)
            .When(x => !string.IsNullOrWhiteSpace(x.SubjectId));
    }
}
