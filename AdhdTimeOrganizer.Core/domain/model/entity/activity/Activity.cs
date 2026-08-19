using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Sydowwe.Framework.domain.audit;
using Sydowwe.Framework.domain.entityInterface;

namespace AdhdTimeOrganizer.Core.domain.model.entity.activity;

public class Activity : BaseEntityWithUser, IBaseNameTextEntity
{
    public required string Name { get; set; }

    [AuditIgnore]
    public string? Text { get; set; }
    public bool IsUnavoidable { get; set; }

    /// <summary>
    /// Retired: the activity keeps every row that points at it and keeps rendering its name on those
    /// rows, but disappears from every <em>picker</em>. This is the lifecycle operation the app never
    /// had — the only one on offer was a hard delete, which cascades (see the FK note below) and
    /// therefore silently destroys the history it was supposed to preserve.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A flag, not a soft delete. <c>ISoftDeletable</c> would add a global query filter and the row
    /// would vanish from the history rows, planner tasks and to-do items that still reference it —
    /// the exact opposite of what archiving is for. The exclusion is applied per endpoint instead, in
    /// the four pickers, and nowhere else; <c>ActivityArchivingTests</c> pins both halves.
    /// </para>
    /// <para>
    /// ⚠ Never write this from <c>ActivityRequest</c>. The create/update form does not carry it, so an
    /// assignment there would silently un-archive on every edit — the same trap
    /// <c>ActivityHistoryRequest</c> documents for its two item links. <c>PATCH /activity/{id}/archived</c>
    /// is the only writer.
    /// </para>
    /// </remarks>
    public bool IsArchived { get; set; }

    public long RoleId { get; set; }
    public virtual ActivityRole Role { get; set; } = null!;
    public long? CategoryId { get; set; }
    public virtual ActivityCategory? Category { get; set; }


    // Activity names NO dependent feature-area type — not to-do items, routine items, history rows,
    // planner tasks, tracker mappings, and since the AdhdTimeOrganizer.ActivityProfiles extraction not
    // the three Activity*Profile rows or MemoryAnchor either. Each of those configures its own
    // Activity FK from the dependent side via the parameterless IsManyWithOneActivity() /
    // IsOneWithOneActivity(); the inverse navigations here only fed those helpers a navigation
    // expression and made the hub reference every feature area. Removing them changed no column,
    // index or cascade — the profile FKs are still unique, still required, still cascade.
    //
    // Query through the DbSet instead (dbContext.Set<ActivityBacklogProfile>().Where(p =>
    // p.ActivityId == ...)), which is what the profile validators in that slice now do. Do not add
    // any of them back: it would make Core reference a slice and invert the whole dependency
    // direction. ActivityProfilesRouteSmokeTests.Core_DoesNotReferenceActivityProfiles pins it.

    public Activity Clone()
    {
        var cloned = (Activity)MemberwiseClone();
        cloned.Id = 0; // Reset ID for new entity
        return cloned;
    }
}