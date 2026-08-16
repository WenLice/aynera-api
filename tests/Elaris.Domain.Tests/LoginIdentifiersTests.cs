using Elaris.Domain.Auth.Enums;
using Elaris.Domain.Auth.Exceptions;
using Elaris.Domain.Auth.Statics;

namespace Elaris.Domain.Tests;

public class LoginIdentifiersTests
{
    [Fact]
    public void Parse_NormalizesIndianMobile()
    {
        var identifier = LoginIdentifiers.Parse("9876543210");
        Assert.Equal(LoginChannel.Phone, identifier.Channel);
        Assert.Equal("+919876543210", identifier.Destination);
        Assert.Equal("phone", identifier.ChannelKey);
    }

    [Fact]
    public void Parse_NormalizesEmail()
    {
        var identifier = LoginIdentifiers.Parse("  Ada@Example.COM ");
        Assert.Equal(LoginChannel.Email, identifier.Channel);
        Assert.Equal("ada@example.com", identifier.Destination);
        Assert.Equal("email", identifier.ChannelKey);
    }

    [Fact]
    public void Parse_RejectsInvalid()
    {
        Assert.Equal("invalid_identifier", Assert.Throws<AuthException>(() => LoginIdentifiers.Parse("12345")).ErrorCode);
        Assert.Equal("invalid_identifier", Assert.Throws<AuthException>(() => LoginIdentifiers.Parse("not-an-email")).ErrorCode);
    }
}
