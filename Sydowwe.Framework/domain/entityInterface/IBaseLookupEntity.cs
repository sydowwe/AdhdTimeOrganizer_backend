namespace Sydowwe.Framework.domain.entityInterface;

/// <summary>
/// The shape shared by both lookup flavors — Framework's host-wide <c>BaseLookup</c> and a host's
/// user-scoped equivalent (<c>BaseLookupWithUser</c>). They can't share a base class because only
/// one of them carries a user, so generic lookup DTOs constrain on this instead.
/// </summary>
public interface IBaseLookupEntity : IBaseTableEntity
{
    public string Text { get; set; }
    public int? SortOrder { get; set; }
}