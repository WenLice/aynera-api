using Aynera.Domain.Common.Validation;

namespace Aynera.Domain.Tests;

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
    [InlineData("member@example.com", true)]
    [InlineData("not-an-email", false)]
    [InlineData("", false)]
    public void BeValidEmail_MatchesExpected(string email, bool expected) =>
        Assert.Equal(expected, RequestValidation.BeValidEmail(email));

    [Theory]
    [InlineData("9876543210", true)]
    [InlineData("member@example.com", true)]
    [InlineData("12345", false)]
    [InlineData("not-an-email", false)]
    public void BeValidLoginIdentifier_MatchesExpected(string identifier, bool expected) =>
        Assert.Equal(expected, RequestValidation.BeValidLoginIdentifier(identifier));

    [Theory]
    [InlineData("Aynera", true)]
    [InlineData("Aynera Professionals", true)]
    [InlineData("AyneraProfessionals", true)]
    [InlineData("Other", false)]
    public void BeKnownEarlyAccessInterest_MatchesExpected(string interest, bool expected) =>
        Assert.Equal(expected, RequestValidation.BeKnownEarlyAccessInterest(interest));
}
