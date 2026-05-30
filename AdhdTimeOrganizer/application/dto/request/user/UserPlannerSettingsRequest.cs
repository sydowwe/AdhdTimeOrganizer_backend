using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.@enum;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record UserPlannerSettingsRequest : IMyRequest<UserPlannerSettings>
{
    [Required]
    public required bool RemindersEnabled { get; init; }

    [Required]
    [Range(0, 120)]
    public required int ReminderMinutesBefore { get; init; }

    [Required]
    public required bool DetailsPanelExpandedByDefault { get; init; }

    [Required]
    public required bool ArrowKeyNavEnabled { get; init; }

    [Required]
    public required List<string> PredefinedSkipReasons { get; init; }

    [Required]
    [Range(1, 120)]
    public required int SlotDurationMinutes { get; init; }

    public long? DefaultApplyTemplateId { get; init; }

    [Required]
    public required ApplyTemplateConflictResolutionEnum DefaultConflictResolution { get; init; }

    [Required]
    public required bool DefaultApplyPreviewMode { get; init; }

    public void UpdateEntity(UserPlannerSettings entity)
    {
        entity.RemindersEnabled = RemindersEnabled;
        entity.ReminderMinutesBefore = ReminderMinutesBefore;
        entity.DetailsPanelExpandedByDefault = DetailsPanelExpandedByDefault;
        entity.ArrowKeyNavEnabled = ArrowKeyNavEnabled;
        entity.PredefinedSkipReasons = PredefinedSkipReasons;
        entity.SlotDurationMinutes = SlotDurationMinutes;
        entity.DefaultApplyTemplateId = DefaultApplyTemplateId;
        entity.DefaultConflictResolution = DefaultConflictResolution;
        entity.DefaultApplyPreviewMode = DefaultApplyPreviewMode;
    }

    public UserPlannerSettings ToEntity => new()
    {
        RemindersEnabled = RemindersEnabled,
        ReminderMinutesBefore = ReminderMinutesBefore,
        DetailsPanelExpandedByDefault = DetailsPanelExpandedByDefault,
        ArrowKeyNavEnabled = ArrowKeyNavEnabled,
        PredefinedSkipReasons = PredefinedSkipReasons,
        SlotDurationMinutes = SlotDurationMinutes,
        DefaultApplyTemplateId = DefaultApplyTemplateId,
        DefaultConflictResolution = DefaultConflictResolution,
        DefaultApplyPreviewMode = DefaultApplyPreviewMode
    };
}
