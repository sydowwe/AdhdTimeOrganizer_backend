using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.domain.model.@enum;

namespace AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;

public class TaskPlannerDayTemplate : BaseEntityWithUser
{
    // Template info
    public required string Name { get; set; } // "HomeOffice", "Office", "Weekend", "Sick Day"
    public string? Description { get; set; }
    public string? Icon { get; set; } // Optional icon/emoji
    public required bool IsActive { get; set; } // Can be disabled without deleting

    public TimeOnly? DefaultWakeUpTime { get; set; }
    public TimeOnly? DefaultBedTime { get; set; }

    // Template customization
    public required DayType SuggestedForDayType { get; set; } // Workday, Weekend, etc.
    public List<DayOfWeek> ScheduledDays { get; set; } = []; // Days of the week this template is intended for
    public Location? SuggestedLocation { get; set; } // Home, Office, Travel
    public List<string> Tags { get; set; } = []; // ["productive", "relaxed", "minimal"]

    /// <summary>
    /// The user pinned this template to the top of their template list. It lives on the template itself
    /// rather than in a "pinned ids" collection because a template already belongs to exactly one user —
    /// so the flag is per-user by construction, and a deleted template takes its pin with it instead of
    /// leaving a dangling id for the client to filter out.
    /// <para>Deliberately not part of <c>TaskPlannerDayTemplateRequest</c>: pinning is its own PATCH, so an
    /// edit submitted from a form that was opened before the pin cannot silently unpin.</para>
    /// </summary>
    public bool IsPinned { get; set; }

    // Usage tracking
    public int UsageCount { get; set; } // How often has this been applied
    public DateTimeOffset? LastUsedAt { get; set; }

    public virtual ICollection<TemplatePlannerTask> Tasks { get; set; } = new List<TemplatePlannerTask>();
}