using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public class TaskPlannerDayTemplate : BaseEntityWithUser
{
    // Template info
    public string Name { get; set; } // "HomeOffice", "Office", "Weekend", "Sick Day"
    public string? Description { get; set; }
    public string? Icon { get; set; } // Optional icon/emoji
    public bool IsActive { get; set; } // Can be disabled without deleting

    public TimeOnly? DefaultWakeUpTime { get; set; }
    public TimeOnly? DefaultBedTime { get; set; }

    // Usage tracking
    public int UsageCount { get; set; } // How often has this been applied
    public DateTimeOffset? LastUsedAt { get; set; }

    // Template customization
    public DayType SuggestedForDayType { get; set; } // Workday, Weekend, etc.
    public List<string> Tags { get; set; } = new(); // ["productive", "relaxed", "minimal"]

    public virtual ICollection<TemplateTask> Tasks { get; set; } = new List<TemplateTask>();
}