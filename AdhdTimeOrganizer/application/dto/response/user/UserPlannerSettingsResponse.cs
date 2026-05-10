using AdhdTimeOrganizer.application.dto.@enum;

namespace AdhdTimeOrganizer.application.dto.response.user;

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
}
