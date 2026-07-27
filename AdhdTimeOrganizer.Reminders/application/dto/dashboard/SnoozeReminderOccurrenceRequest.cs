namespace AdhdTimeOrganizer.Reminders.application.dto.dashboard;

/// <summary>
/// The caller defers <i>their own</i> delivery of one upcoming occurrence to a later instant (phase 05b).
/// Self-scoped: the caller must be an explicit recipient of <see cref="ReminderDefinitionId"/>.
/// </summary>
public class SnoozeReminderOccurrenceRequest
{
    public long ReminderDefinitionId { get; set; }

    /// <summary>The upcoming occurrence instant being snoozed (one of the caller's pending occurrences).</summary>
    public DateTimeOffset OccurrenceAt { get; set; }

    /// <summary>When the deferred delivery should fire for the caller. Must be in the future.</summary>
    public DateTimeOffset SnoozeUntil { get; set; }
}