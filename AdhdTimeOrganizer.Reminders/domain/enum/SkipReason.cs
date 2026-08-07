namespace AdhdTimeOrganizer.Reminders.domain.@enum;

/// <summary>
/// Why a <c>ReminderDispatch</c> with outcome <see cref="DispatchOutcome.Skipped"/> was not sent.
/// Populated by the scanner / dispatch policy (phases 03–04). Internal stored state, NOT part of the
/// <c>Sydowwe.Framework.Contracts</c> surface.
/// </summary>
public enum SkipReason
{
    /// <summary>
    /// Reserved — never written by the current scanner. Dedup against a re-run / misfire / overlap skips an
    /// already-dispatched occurrence silently (no row).
    /// </summary>
    AlreadyDispatched,

    /// <summary>
    /// Reserved — never written by the current scanner. v1 is defer-only: quiet hours defer the dispatch with no
    /// row; reserved for a future drop policy (phase 04).
    /// </summary>
    QuietHours,

    /// <summary>No recipients resolved at dispatch time.</summary>
    NoRecipients,

    /// <summary>The recipient opted out of this reminder kind (phase 04).</summary>
    OptedOut,

    /// <summary>The recipient dismissed this occurrence's delivery (phase 05b).</summary>
    Dismissed,

    /// <summary>The recipient snoozed this occurrence's delivery to a later instant (phase 05b); the re-delivery is its own row.</summary>
    Snoozed,

    /// <summary>
    /// Reserved — never written by the current scanner. A cancelled / paused definition is re-checked and skipped
    /// silently (no row).
    /// </summary>
    Cancelled,

    /// <summary>Any other deliberate skip not covered above.</summary>
    Other
}