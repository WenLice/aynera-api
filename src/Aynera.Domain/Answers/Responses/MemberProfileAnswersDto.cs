using Aynera.Domain.Answers.Records;

namespace Aynera.Domain.Answers.Responses;

/// <summary>
/// The member's everyday, belief and vibe answers. Keys are question and option keys, never
/// labels — the app resolves those against its own question list.
/// </summary>
public sealed record MemberProfileAnswersDto(
    IReadOnlyDictionary<string, MemberAnswer> Lifestyle,
    IReadOnlyDictionary<string, MemberAnswer> Beliefs,
    IReadOnlyList<string> Vibe);
