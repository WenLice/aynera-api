using Aynera.Domain.Common;
using Aynera.Domain.Common.Validators;

namespace Aynera.Domain.Tests;

public class PagedQueryTests
{
    [Fact]
    public void Skip_UsesZeroBasedOffset()
    {
        var query = new PagedQuery { Page = 3, PageSize = 15 };
        Assert.Equal(30, query.Skip);
    }

    [Theory]
    [InlineData(1, 15, true)]
    [InlineData(1, 50, true)]
    [InlineData(0, 15, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, 51, false)]
    public async Task Validator_EnforcesPageBounds(int page, int pageSize, bool expectedValid)
    {
        var result = await new PagedQueryValidator().ValidateAsync(
            new PagedQuery { Page = page, PageSize = pageSize });
        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void PagedResult_ComputesPageFlags()
    {
        var page = new PagedResult<int>([1], 1, 15, 16);
        Assert.Equal(2, page.TotalPages);
        Assert.True(page.HasNextPage);
        Assert.False(page.HasPreviousPage);
    }
}
