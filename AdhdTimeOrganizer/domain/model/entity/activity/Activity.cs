using AdhdTimeOrganizer.domain.model.entity.activity.memoryAnchor;
using AdhdTimeOrganizer.domain.model.entity.activity.profile;
using AdhdTimeOrganizer.domain.model.entity.user;
using Sydowwe.Framework.domain.audit;
using Sydowwe.Framework.domain.entityInterface;

namespace AdhdTimeOrganizer.domain.model.entity.activity;

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


    // Navigation properties — activity-area only.
    //
    // Activity deliberately does NOT name to-do items, routine items, history rows, planner tasks or
    // tracker mappings. Each of those configures its own Activity FK from the dependent side via the
    // parameterless IsManyWithOneActivity() / IsOneWithOneActivity(); the inverse collections here
    // only fed those helpers a navigation expression and made the hub reference every feature area.
    // Removing them changed no column, index or cascade. Query through the DbSet instead
    // (dbContext.PlannerTasks.Where(t => t.ActivityId == ...)).
    public ActivityBacklogProfile? BacklogProfile { get; set; }
    public ActivityProjectProfile? ProjectProfile { get; set; }
    public ActivityBucketListProfile? BucketListProfile { get; set; }
    public virtual ICollection<MemoryAnchor> MemoryAnchors { get; set; } = [];

    public Activity Clone()
    {
        var cloned = (Activity)MemberwiseClone();
        cloned.Id = 0; // Reset ID for new entity
        // Reset navigation references — MemberwiseClone shares them with the source entity.
        cloned.BacklogProfile = null;
        cloned.ProjectProfile = null;
        cloned.BucketListProfile = null;
        cloned.MemoryAnchors = [];
        return cloned;
    }
}