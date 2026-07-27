namespace AdhdTimeOrganizer.Reminders.application.dto.dashboard;

/// <summary>
/// The caller suppresses <i>their own</i> delivery of one upcoming occurrence (phase 05b). Self-scoped: the
/// caller must be an explicit recipient of <see cref="ReminderDefinitionId"/>.
/// </summary>
public class DismissReminderOccurrenceRequest
{
    public long ReminderDefinitionId { get; set; }

    /// <summary>The upcoming occurrence instant being dismissed (one of the caller's pending occurrences).</summary>
    public DateTimeOffset OccurrenceAt { get; set; }
}