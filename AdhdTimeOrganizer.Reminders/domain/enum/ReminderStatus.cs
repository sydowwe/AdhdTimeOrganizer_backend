namespace AdhdTimeOrganizer.Reminders.domain.@enum;

/// <summary>
/// The lifecycle state of a <c>ReminderDefinition</c>. Internal stored state — NOT part of the
/// <c>Kernel.reminders</c> contract. The scanner (phase 03) only acts on <see cref="Active"/> rows;
/// <see cref="Paused"/>/<see cref="Cancelled"/> are control states set by the registry (phase 02),
/// and <see cref="Completed"/> is set when a one-shot's offsets are all dispatched or a recurring
/// reminder passes its end.
/// </summary>
public enum ReminderStatus
{
    /// <summary>Due occurrences are scanned and dispatched.</summary>
    Active,

    /// <summary>Temporarily suspended by the owner; no occurrences dispatch while paused.</summary>
    Paused,

    /// <summary>Withdrawn by the owner (e.g. the subject entity was deleted). Never dispatches; kept so a re-register reactivates the same row.</summary>
    Cancelled,

    /// <summary>Terminal — all occurrences have been produced (one-shot offsets exhausted / recurring schedule ended).</summary>
    Completed
}