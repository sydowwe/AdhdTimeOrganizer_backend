namespace Sydowwe.Framework.infrastructure.persistence.retention;

/// <summary>
/// The shared shape of a ledger-retention policy — one reusable options class every append-only ledger
/// binds from its own config section (<c>SchedulerRetention</c>, <c>ReminderRetention</c>, …). Same shape
/// everywhere; per-ledger <i>values</i> may diverge.
/// <para>
/// <b>Why a shared shape.</b> Scheduler's <c>scheduled_job_run</c>, Reminders' <c>reminder_dispatch</c> /
/// <c>reminder_occurrence_action</c> and the audit trail are the same class of data: technical,
/// append-only, self-auditing. Storage limitation (GDPR Art. 5(1)(e); §13 zák. č. 18/2018 Z. z.) applies
/// to all of them identically, so they answer the same three questions — how long, how many to keep, and
/// whether the purge runs at all.
/// </para>
/// <para>
/// <b>Shape only — the queries stay in the modules.</b> There is deliberately no shared "delete expired"
/// helper. Each ledger's purge is a handful of plain LINQ in its own handler, because the part that
/// actually differs between them is the <c>Restrict</c> FK guard (replay lineage, reversal lineage,
/// cross-ledger references) — the hard, ledger-specific half that no generic primitive can absorb. An
/// earlier version of this file shipped one; it moved the trivial half (age + keep-last-N) into
/// expression-tree machinery that left the type system, while the FK guards stayed duplicated in the
/// callers anyway. Not worth it for four short queries.
/// </para>
/// <para>
/// <b>These windows are operational policy, not statutory figures</b> — deliberately NOT registered in
/// <c>docs/routineLawCheckups.md</c>. If a window ever becomes statutory for a given ledger, register that
/// ledger there and enforce a floor the way <c>EmployeeRetentionOptions</c> does.
/// </para>
/// </summary>
public class RetentionOptions
{
    /// <summary>
    /// The technical-data horizon, aligned with <c>PurgeExpiredAuditLogsJobHandler.AuditLogRetentionYears</c>.
    /// Explicitly NOT the 10-year business-audit/accounting horizon — these ledgers evidence system
    /// behaviour, not business transactions.
    /// </summary>
    public const int DefaultRetentionYears = 3;

    /// <summary>
    /// Per-group floor: how many of a group's most recent rows survive regardless of age. Without it a
    /// rarely-firing job or reminder would lose its entire visible history to the age cutoff alone,
    /// leaving operators with an empty dashboard for something that is working correctly.
    /// </summary>
    public const int DefaultKeepLastN = 20;

    /// <summary>
    /// Kill switch for a deployment that must freeze its ledgers (e.g. during a live dispute or an audit
    /// hold). Disabled means the purge job runs and does nothing — it is never silently unscheduled, so
    /// the dashboard still shows the control exists.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Age horizon in years. Rows older than this are candidates for deletion.</summary>
    public int RetentionYears { get; set; } = DefaultRetentionYears;

    /// <summary>
    /// Per-group keep-last floor. <c>0</c> disables the floor (pure age purge). Ignored by callers that
    /// pass no group selector.
    /// </summary>
    public int KeepLastN { get; set; } = DefaultKeepLastN;

    /// <summary>The age cutoff as a <see cref="DateTime"/> — rows strictly older than this may be deleted.</summary>
    public DateTime CutoffUtc(DateTime? now = null) => (now ?? DateTime.UtcNow).AddYears(-RetentionYears);

    /// <summary>The same cutoff for ledgers whose age column is a <see cref="DateTimeOffset"/>.</summary>
    public DateTimeOffset CutoffOffset(DateTimeOffset? now = null) => (now ?? DateTimeOffset.UtcNow).AddYears(-RetentionYears);
}