using System.Text.RegularExpressions;

namespace Aynera.Domain.Settings.Statics;

/// <summary>
/// The names used in <c>MemberSettings.Visibility</c> — one entry per profile field the member can
/// show or hide. A field with no entry is shown: hiding is the choice that has to be made.
/// </summary>
public static partial class VisibilityKeys
{
    /// <summary>Whether the gender shows on the profile. Hidden or not, matching still uses it.</summary>
    public const string Gender = "gender";

    public static string Lifestyle(string questionKey) => $"lifestyle.{questionKey}";

    public static string Beliefs(string questionKey) => $"beliefs.{questionKey}";

    /// <summary>At most this many fields; a ceiling so a client cannot write an unbounded map.</summary>
    public const int MaxEntries = 200;

    /// <summary>A field name, optionally scoped by its category: <c>gender</c>, <c>lifestyle.drink</c>.</summary>
    public static bool IsValid(string? key) => !string.IsNullOrEmpty(key) && KeyPattern().IsMatch(key);

    /// <summary>Visible unless the member hid it.</summary>
    public static bool IsVisible(IReadOnlyDictionary<string, bool> visibility, string key) =>
        !visibility.TryGetValue(key, out var shown) || shown;

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_-]{0,63}(\.[A-Za-z0-9_-]{1,64})?$")]
    private static partial Regex KeyPattern();
}
