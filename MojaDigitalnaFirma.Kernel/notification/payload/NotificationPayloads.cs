namespace MojaDigitalnaFirma.Kernel.notification.payload;

// One record per NotificationType — the compile-time shape of what each producer may persist.
// Read INotificationPayload first: ids and non-person scalars only, never free-text person data.
// Property names are the persisted camelCase JSON keys (JsonHelper's naming policy) and the renderer
// deserializes these exact records, so renaming one is a storage change: old rows keep the old key and
// deserialize to null, which must still hit a name-less fallback branch in NotificationTextRenderer.
//
// A record here holds ids and non-person scalars only. Anything person-identifying is an overlay written
// onto the JSON at render time by an INotificationPayloadEnricher, never persisted payload.

/// <summary>
/// A dated obligation is coming due. <c>Title</c> is composed by the producing module and must itself be
/// person-data-free — Zmluvy builds it from the register number + the date, never a contract subject.
/// </summary>
[NotificationPayload(NotificationType.DeadlineApproaching)]
public sealed record DeadlineApproachingPayload(string? Title = null) : INotificationPayload;

/// <summary>Dev/QA pipeline check. <c>Message</c> is operator-authored text — keep it PII-free.</summary>
[NotificationPayload(NotificationType.Test)]
public sealed record TestNotificationPayload(string? Message = null) : INotificationPayload;

/// <summary>Aggregated reminder digest: a total plus a count-per-<c>Kind</c> breakdown (non-person category strings).</summary>
[NotificationPayload(NotificationType.ReminderDigest)]
public sealed record ReminderDigestPayload(int Count, IReadOnlyList<ReminderDigestKindCount>? Kinds = null) : INotificationPayload;

/// <param name="Kind">The <c>ReminderDefinition.Kind</c> category string.</param>
public sealed record ReminderDigestKindCount(string Kind, int Count);

/// <summary>A recurring background job failed terminally. Technical identifiers only.</summary>
[NotificationPayload(NotificationType.ScheduledJobFailed)]
public sealed record ScheduledJobFailedPayload(
    string? JobKey = null,
    string? OwnerModule = null,
    string? ErrorType = null,
    long? RunId = null) : INotificationPayload;

/// <summary>
/// A recurring background job never fired (scheduler follow-up 08). Technical identifiers only — and no
/// <c>RunId</c>, because the absence of a run row is the condition being reported. <c>ExpectedRunAt</c> is the
/// fire it missed, so the renderer can say how long it has been silent.
/// </summary>
[NotificationPayload(NotificationType.ScheduledJobOverdue)]
public sealed record ScheduledJobOverduePayload(
    string? JobKey = null,
    string? OwnerModule = null,
    DateTime? ExpectedRunAt = null) : INotificationPayload;