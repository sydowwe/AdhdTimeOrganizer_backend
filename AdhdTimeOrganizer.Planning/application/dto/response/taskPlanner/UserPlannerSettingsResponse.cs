using AdhdTimeOrganizer.Planning.application.dto.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;

public record UserPlannerSettingsResponse
{
    public required bool RemindersEnabled { get; init; }
    public required int ReminderMinutesBefore { get; init; }
    public required bool DetailsPanelExpandedByDefault { get; init; }
    public required bool ArrowKeyNavEnabled { get; init; }
    public required List<string> PredefinedSkipReasons { get; init; }
    public required int SlotDurationMinutes { get; init; }
    public long? DefaultApplyTemplateId { get; init; }
    public required ApplyTemplateConflictResolutionEnum DefaultConflictResolution { get; init; }
    public required bool DefaultApplyPreviewMode { get; init; }

    public static UserPlannerSettingsResponse FromEntity(UserPlannerSettings entity) =>
        new()
        {
            RemindersEnabled = entity.RemindersEnabled,
            ReminderMinutesBefore = entity.ReminderMinutesBefore,
            DetailsPanelExpandedByDefault = entity.DetailsPanelExpandedByDefault,
            ArrowKeyNavEnabled = entity.ArrowKeyNavEnabled,
            PredefinedSkipReasons = entity.PredefinedSkipReasons,
            SlotDurationMinutes = entity.SlotDurationMinutes,
            DefaultApplyTemplateId = entity.DefaultApplyTemplateId,
            DefaultConflictResolution = entity.DefaultConflictResolution,
            DefaultApplyPreviewMode = entity.DefaultApplyPreviewMode
        };
}