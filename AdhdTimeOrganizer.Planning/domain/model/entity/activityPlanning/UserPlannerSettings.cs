using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Planning.application.dto.@enum;
using AdhdTimeOrganizer.Planning.domain.model.@enum;

namespace AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;

public class UserPlannerSettings : BaseEntityWithUser
{
    /// <summary>What a user without a settings row gets for <see cref="AutoReminderMode"/>.</summary>
    public const AutoReminderMode DefaultAutoReminderMode = AutoReminderMode.ImportantOnly;

    /// <summary>What a user without a settings row gets for <see cref="AutoReminderMinImportance"/>: Critical only.</summary>
    public const int DefaultAutoReminderMinImportance = TaskImportance.CriticalMarkerValue;

    /// <summary>
    /// Whether the reminder form is <b>prefilled</b> with <see cref="ReminderMinutesBefore"/> — never a switch over
    /// reminders themselves, the user's own or the automatic ones (<see cref="AutoReminderMode"/>).
    /// </summary>
    public bool RemindersEnabled { get; set; } = true;

    /// <summary>
    /// The lead before a block's start: the form's prefill, and the lead every automatic reminder is created with.
    /// </summary>
    public int ReminderMinutesBefore { get; set; } = 10;

    /// <summary>
    /// Which newly created blocks get a reminder of their own (audit N1). Applies at creation only: a change here
    /// reaches blocks created afterwards, and never adds or removes a reminder on a block that already exists.
    /// </summary>
    public AutoReminderMode AutoReminderMode { get; set; } = DefaultAutoReminderMode;

    /// <summary>
    /// The <see cref="TaskImportance.Importance"/> value a block must reach for
    /// <see cref="AutoReminderMode.ImportantOnly"/> — the user picks one of their own levels, and that level and
    /// everything ranked above it qualifies. A value, not a level id, so deleting or adding a level never orphans it.
    /// </summary>
    public int AutoReminderMinImportance { get; set; } = DefaultAutoReminderMinImportance;

    public bool DetailsPanelExpandedByDefault { get; set; } = true;
    public bool ArrowKeyNavEnabled { get; set; } = true;
    public List<string> PredefinedSkipReasons { get; set; } = [];
    public int SlotDurationMinutes { get; set; } = 10;
    public long? DefaultApplyTemplateId { get; set; }
    public ApplyTemplateConflictResolutionEnum DefaultConflictResolution { get; set; } = ApplyTemplateConflictResolutionEnum.Ignore;
    public bool DefaultApplyPreviewMode { get; set; } = true;

    public TaskPlannerDayTemplate? DefaultApplyTemplate { get; set; }
}
