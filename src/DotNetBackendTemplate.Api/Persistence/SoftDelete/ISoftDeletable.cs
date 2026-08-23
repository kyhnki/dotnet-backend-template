namespace DotNetBackendTemplate.Api.Persistence.SoftDelete;

/// <summary>
/// Opts an entity into soft delete. Deletes become updates, and a global query filter
/// hides deleted rows from every query that does not call <c>IgnoreQueryFilters()</c>.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }

    DateTimeOffset? DeletedAtUtc { get; set; }
}
