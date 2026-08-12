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