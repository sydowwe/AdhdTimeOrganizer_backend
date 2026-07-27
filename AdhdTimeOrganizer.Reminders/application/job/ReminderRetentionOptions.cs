using Sydowwe.Framework.infrastructure.persistence.retention;

namespace AdhdTimeOrganizer.Reminders.application.job;

/// <summary>
/// Retention policy for the Reminders module's append-only ledgers (<c>reminder_dispatch</c>,
/// <c>reminder_occurrence_action</c>) and its long-terminal <c>reminder_definition</c> rows. Bound from the
/// "ReminderRetention" config section; the shared defaults (3 years, keep last 20 per definition) work out
/// of the box. See <see cref="RetentionOptions"/> for why the shape is shared with Scheduler.
/// </summary>
public sealed class ReminderRetentionOptions : RetentionOptions
{
    public const string SectionName = "ReminderRetention";

    /// <summary>
    /// Whether the purge also collects long-terminal reminder definitions (<c>Cancelled</c> /
    /// <c>Completed</c>) once nothing in either ledger still references them. Default <c>true</c>.
    /// <para>
    /// Separate from <see cref="RetentionOptions.Enabled"/> because it is a different <i>kind</i> of
    /// delete: the ledgers are <c>[NoAudit]</c> operational history, whereas a definition is an audited
    /// registry row that a producer module could in principle re-register. A deployment that wants the
    /// ledgers trimmed but the registry kept intact turns this off on its own.
    /// </para>
    /// </summary>
    public bool PurgeTerminalDefinitions { get; set; } = true;
}