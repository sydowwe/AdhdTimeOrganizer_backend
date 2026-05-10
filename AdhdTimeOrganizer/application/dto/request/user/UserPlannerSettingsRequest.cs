using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.@enum;
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record UserPlannerSettingsRequest : IMyRequest
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
}
