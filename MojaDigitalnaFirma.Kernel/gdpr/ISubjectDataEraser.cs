namespace MojaDigitalnaFirma.Kernel.gdpr;

/// <summary>
/// Erases one module's <b>recipient-side</b> personal data for a single data subject, on demand
/// (GDPR Art. 17). It points <i>inward</i>: the host's erasure path fans out to every module that holds
/// rows about the subject.
/// <para>
/// <b>Why a Kernel fan-out and not a direct call.</b> The erasure path lives in the Employee module,
/// which must never reference <c>Core.Reminders</c> / <c>Core.Notifications</c>. Implementations are
/// composed as <c>IEnumerable&lt;ISubjectDataEraser&gt;</c> exactly as
/// <c>IEmployeePersonalDataProvider</c> is on the read side, so a host that ships neither module
/// resolves an empty enumerable and the loop is a no-op.
/// </para>
/// <para>
/// <b>Why the retention purges do not cover this.</b> Retention deletes by <i>age</i>; erasure deletes by
/// <i>subject, on demand</i>. Someone anonymized today has rows from last week that a 3-year purge will
/// not touch for years — different axis, different mechanism.
/// </para>
/// <para>
/// <b>Contract — same rules as <c>EmployeeErasureService</c> itself:</b> an implementation mutates
/// tracked entities on the <b>ambient <c>DbContext</c></b> and does <b>NOT</b> commit — the caller
/// (anonymize endpoint or retention job) owns the transaction and <c>SaveChanges</c>. Prefer tracked
/// <c>Remove</c>/property edits over <c>ExecuteDelete/UpdateAsync</c>, which commit immediately and
/// bypass the audit interceptor.
/// </para>
/// <para>
/// <b>Scope — recipient-side only.</b> Erase rows <i>addressed to</i> the subject (their bell history,
/// their preferences, their push subscriptions, their snooze/dismiss actions, their membership in a
/// recipient list). Do <b>NOT</b> scrub rows <i>about</i> the subject that belong to someone else — e.g. a
/// "probation ending" notification in a manager's bell. Those payloads are minimized to ids by
/// <c>INotificationPayloadEnricher</c> and resolved to names at <i>render</i> time precisely so an
/// anonymized employee degrades on its own, with no backfill and no payload scrubber. Building one here
/// would duplicate that design and fight the enricher.
/// </para>
/// <para>
/// <b>Failure policy: throw.</b> Implementations must let exceptions escape rather than swallowing them.
/// A silently skipped erasure is an Art. 17 compliance hole that nobody discovers until an audit, and the
/// caller runs inside a transaction it will roll back — so a loud failure leaves the subject un-erased and
/// visibly retryable, rather than half-erased and believed done. This deliberately differs from
/// <c>ILeaveAttachmentEraser</c>, which returns failures for dead-lettering: that one talks to remote file
/// storage, where a transient outage is expected and the DB pointer removal is still worth committing.
/// These implementations touch only the local database, where a failure is never routine.
/// </para>
/// </summary>
public interface ISubjectDataEraser
{
    /// <summary>PII-free module identifier used in the erasure audit line / log (e.g. <c>"Reminders"</c>).</summary>
    string ModuleKey { get; }

    /// <summary>
    /// Erases this module's recipient-side rows for the subject and returns the number of rows affected
    /// (deleted + pseudonymized) for the erasure audit trail. Must be idempotent — running it twice for the
    /// same subject erases nothing the second time and returns 0.
    /// </summary>
    Task<int> EraseSubjectDataAsync(SubjectErasureRequest request, CancellationToken ct);
}

/// <summary>
/// The subject to erase, carrying <b>both</b> ids: the Employee module erases by <see cref="EmployeeId"/>,
/// while Reminders and Notifications key everything by <see cref="UserId"/>. <c>Employee.UserId</c> is a
/// <c>required long</c>, so the bridge is available at the call site — it is resolved once there rather
/// than making every module re-derive it (which would cost each module a reference to the Employee entity).
/// </summary>
/// <param name="EmployeeId">The employee being anonymized.</param>
/// <param name="UserId">That employee's login user id — the key every recipient-side row uses.</param>
public record SubjectErasureRequest(long EmployeeId, long UserId);