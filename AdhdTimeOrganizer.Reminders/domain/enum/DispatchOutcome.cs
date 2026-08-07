namespace AdhdTimeOrganizer.Reminders.domain.@enum;

/// <summary>
/// The outcome recorded on an append-only <c>ReminderDispatch</c> row — what actually happened when the
/// scanner (phase 03) acted on one occurrence. Internal stored state, NOT part of the <c>Sydowwe.Framework.Contracts</c> surface.
/// </summary>
public enum DispatchOutcome
{
    /// <summary>Dispatched to the Notification module successfully.</summary>
    Sent,

    /// <summary>Deliberately not sent — see the row's <c>SkipReason</c>.</summary>
    Skipped,

    /// <summary>An error occurred while dispatching (logged, not silently dropped).</summary>
    Failed,

    /// <summary>A correction row that reverses an earlier dispatch (links back via <c>ReversesDispatchId</c>).</summary>
    Reversed
}