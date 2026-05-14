using AdhdTimeOrganizer.application.dto.@enum;
using AdhdTimeOrganizer.domain.model.entity.user;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public class UserPlannerSettings : BaseEntityWithUser
{
    public bool RemindersEnabled { get; set; } = true;
    public int ReminderMinutesBefore { get; set; } = 10;
    public bool DetailsPanelExpandedByDefault { get; set; } = true;
    public bool ArrowKeyNavEnabled { get; set; } = true;
    public List<string> PredefinedSkipReasons { get; set; } = [];
    public int SlotDurationMinutes { get; set; } = 10;
    public long? DefaultApplyTemplateId { get; set; }
    public ApplyTemplateConflictResolutionEnum DefaultConflictResolution { get; set; } = ApplyTemplateConflictResolutionEnum.Ignore;
    public bool DefaultApplyPreviewMode { get; set; } = true;

    public TaskPlannerDayTemplate? DefaultApplyTemplate { get; set; }
}
