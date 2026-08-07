namespace AdhdTimeOrganizer.Reminders.domain.@enum;

/// <summary>
/// The kind of per-recipient action recorded on an append-only <c>ReminderOccurrenceAction</c> row
/// (phase 05b). Internal stored state, NOT part of the <c>Sydowwe.Framework.Contracts</c> surface.
/// </summary>
public enum ReminderActionType
{
    /// <summary>The recipient deferred <i>their own</i> delivery of one occurrence to <c>SnoozeUntil</c>.</summary>
    Snooze,

    /// <summary>The recipient suppressed <i>their own</i> delivery of one occurrence entirely.</summary>
    Dismiss,

    /// <summary>A correction row that reverses an earlier action (links back via <c>ReversesActionId</c>).</summary>
    Reversal
}