namespace MojaDigitalnaFirma.Kernel.reminders;

/// <summary>
/// The idempotency identity of a reminder: the 4-tuple keying every registration, dispatch-log row and
/// test assertion. A <see cref="ReminderRegistration"/> is an upsert by this key — re-registering the
/// same key updates the existing definition in place, never creating a duplicate.
/// <para>
/// Modelled as a <c>record</c> so equality is structural (same 4-tuple ⇒ equal): the idempotency check
/// in the registry (phase 02) and the dedup checks in the scanner (phase 03) rely on this value equality.
/// </para>
/// </summary>
/// <param name="OwnerModule">The module that owns the reminder, e.g. "EmployeeModule".</param>
/// <param name="SubjectType">The kind of entity the reminder is about, e.g. "Employee".</param>
/// <param name="SubjectId">
/// The subject entity's id as a string — callers pass <c>entity.Id.ToString()</c>. Pinned to string so
/// the 4-tuple and its DB index are a single type with no numeric/string overloads.
/// </param>
/// <param name="Kind">The reminder's semantic kind within the owner, e.g. "ProbationEnding".</param>
public sealed record ReminderKey(string OwnerModule, string SubjectType, string SubjectId, string Kind);