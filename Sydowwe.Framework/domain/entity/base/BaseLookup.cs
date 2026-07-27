using Sydowwe.Framework.domain.entityInterface;

namespace Sydowwe.Framework.domain.entity.@base;

/// <summary>
/// A lookup ("reference"/"code") table: a small, host-wide set of allowed values referenced by FK
/// and surfaced to the UI as select options. Not user-scoped — see the host's own
/// <c>BaseLookupWithUser</c> when each user owns their own set of values.
/// </summary>
public abstract class BaseLookup : BaseTableEntity, IBaseLookupEntity
{
    // Not `required`: LookupRequest<TEntity> constructs these through a `new()` constraint, which
    // C# forbids on types with required members.
    public string Text { get; set; } = null!;
    public int? SortOrder { get; set; }
}
