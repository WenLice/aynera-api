namespace Elaris.Application.Features.Auth.Models;

public sealed class AdminSeedOptions
{
    public const string SectionName = "Elaris:AdminSeed";

    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Password { get; set; }
}
