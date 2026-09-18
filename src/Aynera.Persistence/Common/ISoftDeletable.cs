namespace Aynera.Persistence.Common;

/// <summary>
/// Every durable app table should implement soft delete.
/// Set <see cref="IsDeleted"/> (and usually <see cref="DeletedAtUtc"/>) instead of removing the row.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTimeOffset? DeletedAtUtc { get; set; }
}
