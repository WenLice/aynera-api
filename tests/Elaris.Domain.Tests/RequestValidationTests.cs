using Elaris.Domain.Common.Validation;

namespace Elaris.Domain.Tests;

public class RequestValidationTests
{
    [Theory]
    [InlineData("9876543210", true)]
    [InlineData("+919876543210", true)]
    [InlineData("919876543210", true)]
    [InlineData("12345", false)]
    [InlineData("", false)]
    public void BeValidIndianMobile_MatchesExpected(string phone, bool expected) =>
        Assert.Equal(expected, RequestValidation.BeValidIndianMobile(phone));

    [Theory]
    [InlineData("Elaris", true)]
    [InlineData("Elaris Professionals", true)]
    [InlineData("ElarisProfessionals", true)]
    [InlineData("Other", false)]
    public void BeKnownEarlyAccessInterest_MatchesExpected(string interest, bool expected) =>
        Assert.Equal(expected, RequestValidation.BeKnownEarlyAccessInterest(interest));
}
