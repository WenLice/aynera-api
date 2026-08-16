using Elaris.Domain.Common;
using FluentValidation;

namespace Elaris.Domain.Common.Validators;

public sealed class PagedQueryValidator : AbstractValidator<PagedQuery>
{
    public PagedQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page must be at least 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, PagedQuery.MaxPageSize)
            .WithMessage($"Page size must be between 1 and {PagedQuery.MaxPageSize}.");
    }
}
