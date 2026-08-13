using System.Text.Json;

namespace Elaris.Domain.Audit.Statics;

public static class AuditRedaction
{
    public static string MaskPhone(string? phoneE164)
    {
        if (string.IsNullOrWhiteSpace(phoneE164))
        {
            return string.Empty;
        }

        var digits = new string(phoneE164.Where(char.IsDigit).ToArray());
        if (digits.Length < 4)
        {
            return "****";
        }

        var last4 = digits[^4..];
        var prefix = phoneE164.StartsWith('+') ? "+" : string.Empty;
        var country = digits.Length > 10 ? digits[..^10] : string.Empty;
        return $"{prefix}{country}******{last4}";
    }

    public static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var at = email.IndexOf('@');
        if (at <= 1)
        {
            return "***";
        }

        return $"{email[0]}***{email[at..]}";
    }
}

/// <summary>Builds field-level old/new maps for AuditEvents.Changes.</summary>
public static class AuditChanges
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Dictionary<string, AuditFieldChange> Create(
        IEnumerable<(string Field, object? OldValue, object? NewValue)> fields)
    {
        var map = new Dictionary<string, AuditFieldChange>(StringComparer.Ordinal);
        foreach (var (field, oldValue, newValue) in fields)
        {
            if (Equals(oldValue, newValue))
            {
                continue;
            }

            map[field] = new AuditFieldChange(oldValue, newValue);
        }

        return map;
    }

    public static string? ToJson(IReadOnlyDictionary<string, AuditFieldChange>? changes) =>
        changes is null || changes.Count == 0
            ? null
            : JsonSerializer.Serialize(changes, JsonOptions);

    public static string? MetadataToJson(object? metadata) =>
        metadata is null
            ? null
            : JsonSerializer.Serialize(metadata, JsonOptions);
}

public sealed record AuditFieldChange(object? Old, object? New);
