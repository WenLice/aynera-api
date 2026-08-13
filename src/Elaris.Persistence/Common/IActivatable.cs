namespace Elaris.Persistence.Common;

/// <summary>
/// Entities that can be deactivated without soft-deleting.
/// Set <see cref="IsActive"/> to false and stamp <see cref="DeactivatedAtUtc"/>.
/// </summary>
public interface IActivatable
{
    bool IsActive { get; set; }
    DateTimeOffset? DeactivatedAtUtc { get; set; }
}
