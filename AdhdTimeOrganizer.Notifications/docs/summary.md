# Notifications — Agent Summary

**Purpose:** Cross-cutting notifications. Reaches users **while the app is open**
(live, via SignalR), **while it is closed** (background, via Web Push), and **when they never install it at all** (Email/SMTP — the realistic case for approvers in a <100-person company, and the only
channel iOS users get outside an installed PWA). Triggered by any module — background jobs (low stock, deadlines) or ad-hoc events.

**Bounded context:** Owns notification delivery, history, per-user preferences, and push subscriptions. Does NOT own the business events that trigger notifications — callers decide *when* to notify.

## Dependency seams

- **Consumes:** `UserManager<User>` for recipient resolution. No inbound module deps.
- **Exposes:** `INotificationService.NotifyAsync(...)` via the **Kernel contract**
  (`MojaDigitalnaFirma.Kernel.notification`). Any module or Quartz job calls it; the impl implements `IScopedService` so it's auto-registered, and it's safe to call with no authenticated user.
- **Cross-module contracts (in Kernel):** `INotificationService`, `NotificationType`,
  `NotificationChannel`, `NotificationRecipients`, `INotificationPayloadEnricher`, and — since the quiet-hours consolidation — `IQuietHoursReader` + `QuietHoursWindow` + `QuietHoursPolicy`
  (this module owns the table; Reminders reads it through the seam).

## Gotchas — things that will bite you

- A `Notification` row stores `Type` + a JSON `payload`, **not** finished text — it stays locale-agnostic. Text is produced at send/read time by
  `INotificationTextRenderer`. **Your payload's properties must match what the renderer reads for that type**, or the rendered title/body will be wrong/empty.
- **Payloads must carry ids, never person names — and this is no longer a convention, it is the type system.** A notification row belongs to its **recipient** (HR, the manager), so GDPR erasure of the
  person *named inside it* never touches it — a stored
  `employeeName` would be frozen PII surviving anonymization forever (audit 2026-07, L1). Persist `employeeId`; the display name is resolved per read by
  `INotificationPayloadEnricher` (Kernel), so an anonymized employee degrades on its own with no backfill or scrubber. Enrichment is **best-effort** — a caller with no ambient
  `HttpContext` (background job) gets the renderer's name-less fallback rather than an exception.
    - You **cannot** pass an anonymous object any more. `NotifyAsync` takes
      `INotificationPayload`, and there is one record per `NotificationType` in
      `Kernel/notification/payload/` — the rule itself is stated once, in
      `INotificationPayload`'s XML doc, and every payload property in all three modules points at it instead of paraphrasing. Adding a type means adding its record.
    - `PayloadPiiContractGuardTests` reflects over every payload record and **fails the build** on a person-data property name (SK + EN). A typed record only helps if nobody can add `EmployeeName` to
      it tomorrow; that test is what stops them.
    - **There is no Reminders exception.** `RenderedReminder.Payload` and
      `ReminderRegistration.Payload` are markered too, so an owning module's renderer obeys the same contract as a direct producer. The two `Raw*Payload` wrappers are *rehydration* of an
      already-persisted document, not an authoring escape hatch.
- Preferences are **absence-means-the-default**, and the default is *not* uniform:
  InApp/WebPush are on for every type, **Email is per-type** (`NotificationChannelDefaults`
  — on for approvals/compliance/deadlines, off for `ReminderDigest` / `UpcomingHrEvents` /
  `Test` / anything unknown). An explicit `NotificationPreference` row overrides in both directions. If *all three* channels are off for a recipient, they're skipped entirely — no history row is
  written.
- **Adding a `NotificationType` is now a two-file change**: the type itself, and its Email entry in `NotificationChannelDefaults`. Forget the second and email silently defaults OFF.
- ⚠️ **Legal (§116 zák. 452/2021):** the Email default-ON set is lawful only because every member is an *operational* notification about the recipient's own work. Any future **marketing** type must be
  consent-based (opt- **IN**) — never add one to the default-on matrix, and never reuse this channel for promotional content.
- The Email channel **short-circuits when SMTP is unconfigured** (`EmailNotificationOptions`
  auto-detects the `MAIL_*` env vars **and `API_URL`** — the framework sender's ctor reads all of them). `EmailSenderService` throws in its *constructor* on any missing var, so it is registered as a
  factory and resolved only after that guard passes — don't "simplify" it into a constructor injection.
- **Email addresses are resolved *before* the parallel channel fan-out, on purpose.**
  `UserManager` is backed by the dispatcher's own scoped `DbContext`
  (`AddEntityFrameworkStores`), the same one the Web Push branch reads. A `DbContext` can't run two queries at once, so doing the address lookup inside the parallel `Task.WhenAll` would race the
  push-subscription read and throw intermittently *only when both channels are configured*. Keep DB reads out of the concurrent region — only the Web Push branch may touch the context there.
- **Quiet hours defer the background channels, they never drop them.** A recipient with a
  `NotificationQuietHours` window who is inside it at send time gets the history row **and** the InApp (SignalR) push immediately — only Web Push + Email are withheld, by stamping
  `Notification.DeferredUntil`
  with the instant the window ends. `Notifications.FlushDeferredNotifications` (every 15 min) delivers those and clears the column. **The bell / `mine` list ignores `DeferredUntil` entirely** — a
  deferred notification is visible in-app right away, so don't "fix" the read path to hide it.
    - **One window per user, deployment-wide, and this module owns it.** Reminders used to have its own
      `ReminderQuietHours` table; it was migrated into `notification_quiet_hours` and now reads it through
      `Kernel.notification.IQuietHoursReader`. Editing is at `GET|PUT|DELETE /notification-quiet-hours`
      (owner-scoped, no user id anywhere in the route) — **not** under `/reminder-preference` any more.
    - Minutes-from-local-midnight in `Application:Timezone` (the repo models **no** per-user zone).
      `Start > End` = overnight; `Start == End` is rejected — clear the window with `DELETE` instead.
    - **Opt-in**: no row = no quiet hours, and nothing seeds a default (L5 is posture, not obligation).
    - The deferral is **frozen at send time**: editing or clearing the window afterwards does not move an already-stamped `DeferredUntil`. Preferences, on the other hand, are re-checked at flush
      time — a channel switched off during the window is honoured.
    - `DeferredUntil` is only stamped for recipients who would actually get Web Push or Email; an InApp-only recipient is never deferred, so the flush job is never handed a row with nothing to
      deliver.
- `Notification` / `PushSubscription` / `NotificationPreference` / `NotificationQuietHours` are all `[NoAudit]`
  (high volume / opaque keys). For an audit trail of a semantic event, call
  `IAuditService.LogAsync` from the **triggering** code, not these rows.
- SignalR hub auth uses the httpOnly `auth-token` cookie — the SPA connects with
  `withCredentials: true` and **no** `accessTokenFactory`.
- iOS receives Web Push only when the app is installed as a PWA. Stale push subscriptions are pruned lazily (dispatcher deletes on a `404/410`).
- **History expires.** A daily job (`Notifications.PurgeExpiredHistory`) hard-deletes read notifications past **90d**, all notifications past **365d**, and push subscriptions idle past **180d**. So
  don't treat `notification` as a durable ledger — if a caller needs a permanent trail of the event, it must write one itself via `IAuditService.LogAsync`. Windows are owner policy (consts on the
  handler), not statutory. Preferences are exempt.
- **A GDPR erasure also reaches this module, on demand.**
  `application/service/NotificationSubjectDataEraser.cs` implements the Kernel fan-out
  `MojaDigitalnaFirma.Kernel/gdpr/ISubjectDataEraser.cs`, which `EmployeeErasureService` composes as `IEnumerable<ISubjectDataEraser>` (no cross-module reference either way). It **deletes** all four
  of the subject's own tables — `Notification`, `NotificationPreference`, `PushSubscription`,
  `NotificationQuietHours` — because none is an append-only ledger. Different axis from the purge above: that one deletes by **age**, this one by **subject**. Nothing else would collect these rows:
  deactivating a user never deletes the `CoreUser` row, so no cascade fires. **Recipient-side only** (`WHERE UserId = <erased user>`) — a notification *about* the erased employee in someone else's
  bell is untouched and degrades through the enricher, per the payload rule above. Do not turn this into a payload scrubber.
- The purge measures a subscription's staleness by `ModifiedTimestamp`, which
  `SubscribePushEndpoint` **force-touches on every re-POST** even when nothing changed. If you refactor that endpoint, keep the touch — dropping it silently prunes live devices.
- The module's recurring jobs (`Notifications.PurgeExpiredHistory`, `Notifications.FlushDeferredNotifications`)
  are declared together in `NotificationsScheduledJobsRegistrar.Registrations` and only run on hosts that wire the Quartz substrate; the vanilla Sandbox no-ops. **Adding one is a one-entry change
  there** — but note the registrar's registration list is no longer a single item, so don't assert `ContainSingle()` over it.

## Extension playbook

- **Add a notification type:** 1) add a member to `NotificationType` (Kernel);
    2) add its payload record in `Kernel/notification/payload/NotificationPayloads.cs`, tagged
       `[NotificationPayload(NotificationType.YourType)]` — **ids and non-person scalars only**
       (the guard test enforces it, and `EveryNotificationType_HasExactlyOnePayloadRecord` will fail until the record exists);
    3) add a `case` in `NotificationTextRenderer.Render` deserializing that record — give it a **name-less fallback branch**, since enrichment can be absent;
    4) decide its **Email default** in `NotificationChannelDefaults` (omit it and email is OFF);
    5) call the typed `NotifyAsync(recipients, payload, ct)` from the trigger. No migration needed (enum stored as string via `EnumColumn`; the payload record's property names *are*
       the persisted camelCase keys, so renaming one is a storage change).
- **Emit a notification from another module:** inject `INotificationService` and call the typed overload — the `NotificationType` is derived from the payload record's attribute, so there is no way to
  pair the wrong type with the wrong payload:

```csharp
public class LowStockJob(INotificationService notifications)
{
    public async Task RunAsync(StockItem item, CancellationToken ct) =>
        await notifications.NotifyAsync(
            NotificationRecipients.Admins(),               // who
            new StockLowPayload(item.Name, item.Id),       // what + payload, one argument
            ct);
}
```

The untyped `NotifyAsync(recipients, type, payload, ct)` overload still exists for the one runtime-dispatch caller (the Reminders scan, which reads its type and payload back out of the DB) — but its
payload parameter is `INotificationPayload`, not `object?`, so it is not a way around the rule.

## Deeper reference

- `domain-map.md` — architecture, entities, recipients, endpoints, navigation index
- `testing.md` — test strategy, what's covered, and remaining gaps
- Setup (VAPID keys, config): [`../docs/notificationSetup.md`](../docs/notificationSetup.md)
