using Aynera.Domain.Auth.Enums;

namespace Aynera.Domain.Auth.Requests;

/// <summary>
/// One-shot registration: every basic detail at once. <paramref name="Name"/> may be a first name or a
/// full name — whatever the member calls themselves.
/// </summary>
public sealed record CreateMemberRequest(
    string Phone,
    string Name,
    Gender Gender,
    DateOnly DateOfBirth,
    string City,
    string Email,
    string? Nickname = null,
    int? HeightCm = null,
    string? Hometown = null,
    string? Work = null,
    string? Religion = null,
    bool GenderIsPublic = true,
    string? Password = null);
