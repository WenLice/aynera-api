using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Common;
using FluentValidation;

namespace Aynera.Domain.Auth.Validators;

public sealed class MemberAdminListQueryValidator : AbstractValidator<MemberAdminListQuery>
{
    public const int MaxSearchLength = 100;

    public MemberAdminListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, PagedQuery.MaxPageSize)
            .WithMessage($"Page size must be between 1 and {PagedQuery.MaxPageSize}.");
        RuleFor(x => x.Search)
            .MaximumLength(MaxSearchLength)
            .When(x => !string.IsNullOrWhiteSpace(x.Search));
    }
}
