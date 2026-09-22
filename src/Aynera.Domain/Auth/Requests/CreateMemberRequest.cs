using Aynera.Domain.Auth.Enums;

namespace Aynera.Domain.Auth.Requests;

/// <summary>
/// One-shot registration: every basic detail at once. <paramref name="Name"/> may be a first name or a
/// full name — whatever the member calls themselves.
/// </summary>
/// <param name="Hometown">Where the member is from. Required, though it carries a default so the wire
/// shape stays optional and a missing one is answered by validation rather than by a binding error.</param>
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
