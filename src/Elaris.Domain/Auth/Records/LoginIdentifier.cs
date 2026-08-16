using Elaris.Domain.Auth.Enums;

namespace Elaris.Domain.Auth.Records;

public sealed record LoginIdentifier(LoginChannel Channel, string Destination)
{
    public string ChannelKey => Channel switch
    {
        LoginChannel.Phone => "phone",
        LoginChannel.Email => "email",
        _ => throw new InvalidOperationException($"Unknown login channel {Channel}.")
    };
}
