using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Planning.application.dto.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;

public record UserPlannerSettingsRequest : IMyRequest<UserPlannerSettings>
{
    public required bool RemindersEnabled { get; init; }


    [Range(0, 120)]
    public required int ReminderMinutesBefore { get; init; }


    public required bool DetailsPanelExpandedByDefault { get; init; }


    public required bool ArrowKeyNavEnabled { get; init; }


    public required List<string> PredefinedSkipReasons { get; init; }


    [Range(1, 120)]
    public required int SlotDurationMinutes { get; init; }

    public long? DefaultApplyTemplateId { get; init; }


    public required ApplyTemplateConflictResolutionEnum DefaultConflictResolution { get; init; }


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
        UserId = 0,
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