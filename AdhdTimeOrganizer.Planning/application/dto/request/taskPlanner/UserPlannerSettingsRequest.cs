using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Planning.application.dto.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;

public record UserPlannerSettingsRequest : IMyRequest<UserPlannerSettings>
{
    public required bool RemindersEnabled { get; init; }


    [Range(0, 120)]
    public required int ReminderMinutesBefore { get; init; }

    /// <summary>Which new blocks get a reminder automatically.</summary>
    public required AutoReminderMode AutoReminderMode { get; init; }


    /// <summary>The importance value a block must reach in <c>ImportantOnly</c> mode — one of the user's own levels.</summary>
    public required int AutoReminderMinImportance { get; init; }


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
        entity.AutoReminderMode = AutoReminderMode;
        entity.AutoReminderMinImportance = AutoReminderMinImportance;
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
        AutoReminderMode = AutoReminderMode,
        AutoReminderMinImportance = AutoReminderMinImportance,
        DetailsPanelExpandedByDefault = DetailsPanelExpandedByDefault,
        ArrowKeyNavEnabled = ArrowKeyNavEnabled,
        PredefinedSkipReasons = PredefinedSkipReasons,
        SlotDurationMinutes = SlotDurationMinutes,
        DefaultApplyTemplateId = DefaultApplyTemplateId,
        DefaultConflictResolution = DefaultConflictResolution,
        DefaultApplyPreviewMode = DefaultApplyPreviewMode
    };
}