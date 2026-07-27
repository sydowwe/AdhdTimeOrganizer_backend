# Notifications — Testing

## How to test this module

Standard portal test setup — see root [`../docs/testing.md`](../docs/testing.md): real
`Program` against a Postgres Testcontainer, `[Collection("Postgres")]`, `RoleTestAuthHandler`, client-factory helpers. Tests live under `MojaDigitalnaFirma.AdminPortal.Tests/integration/`
(`endpoint/notification/`, `service/notification/`).

Two collaborators are faked rather than exercised for real — never assert a real WebSocket round-trip or outbound push HTTP:

- **SignalR**: `IHubContext<NotificationHub>` is replaced by `fakes/FakeHubContext.cs`, which records `Clients.User(id).SendAsync(method, …)` calls so the dispatcher's targeting can be asserted.
- **Web Push**: `IWebPushSender` is faked; assert one call per `PushSubscription` and that a
  `true` (gone) return prunes the subscription row.
- **Email**: `INotificationEmailSender` is faked (`fakes/FakeNotificationEmailSender.cs`), which records `(email, subject, htmlBody)` and can be told to throw for a chosen address. Always set
  `services.Configure<EmailNotificationOptions>(o => o.Enabled = true/false)` explicitly — the real guard auto-detects the `MAIL_*` env vars and tests must never depend on the machine's env.

## Strategy

The endpoints are plain `Endpoint<>`s (not the CRUD bases), so tests are concrete rather than the `Base*EndpointTests` matrix. Cover: auth matrix, owner-scoping / IDOR, and the dispatcher's recipient
resolution + preference logic.

Covered today:

- **`NotificationService` (dispatcher)** — recipient resolution (`Everyone`/`InRole`/`User`/
  `Users`/empty), preference opt-out per channel, both-channels-off ⇒ no row, missing prefs ⇒ enabled, payload serialization, SignalR + Web Push dispatch, gone-subscription pruning. Renderer failure
  is asserted via `RendererThrows_ExceptionSwallowed_NoRowsWritten`: the exception is swallowed (never propagates to the caller), no rows are written.
- **`NotificationTextRenderer`** (unit) — per-type title/body for all known `NotificationType`
  values, generic fallback for unknown types (CQ-3: no raw JSON leak), malformed/empty/non-string payload resilience.
- **`WebPushSender`** (unit) — status-code → bool/throw mapping: 201 → false (keep), 410/404 → true (prune), 5xx → throws. Uses a programmatic P-256 VAPID key pair and a fake `HttpMessageHandler`.
- **`GET /notification/mine`** — owner-scoping, newest-first, ≤50 cap, rendered title/body.
- **Payload minimization** (`NotificationPayloadEnrichmentTests`) — `LeavePending` /
  `WorkLogComplianceBreach` payloads holding only `employeeId` render the full name; an employee anonymized *after* the row was written no longer renders it; a 50-row page across several employees
  resolves each correctly; payloads with no `employeeId` skip the read seam entirely (asserted by reference-identity of the returned list — only the fast path returns the input instance); an
  unresolvable id falls back to the name-less text. The write side (call sites persisting `employeeId`, never `employeeName`) is asserted in `AttendanceNotificationTests`.
- **`PATCH /notification/{id}/read`** — happy path + `ReadAt`, idempotent, unknown id 404, IDOR.
- **`GET /notification/mine` pagination** (`beforeId`/`limit`) — page 2 via `beforeId` yields the next-older rows with no overlap, no-params behavior unchanged (≤50), `limit` clamps to 100, a page
  never surfaces another user's rows regardless of `beforeId`.
- **`GET /notification/unread-count`** — counts only the caller's unread rows, ignores other users'.
- **`POST /notification/read-all`** — marks all the caller's unread rows read + `ReadAt`, idempotent with nothing unread, never touches another user's rows.
- **`DELETE /notification/{id}`** — happy path (row gone), unknown id 404, IDOR (other user's row survives).
- **`GET /notification-preference/mine`** — one entry per (type, channel) pair (39 today); no-row rows report the per-type default (InApp/WebPush all-on, Email per `NotificationChannelDefaults`
  — `LeavePending` ON / `ReminderDigest` OFF asserted); an explicit disabled row overrides a default-on channel and an explicit enabled row overrides a default-off one; another user's explicit row
  never leaks into the caller's matrix.
- **`PUT /notification-preference`** — upsert (create/update, no duplicate), per-user scoping.
- **`POST /notification/test`** — Admin/RootAdmin 204 + row persisted; Employee 403; unauthenticated 401.
- **`POST /push-subscription`** — create row owned by caller, re-POST same endpoint updates (no duplicate), re-POST by a different user re-owns the row; auth. SSRF guard (HTTP scheme, loopback,
  RFC-1918 private IPs, link-local 169.254/16, unresolvable host allowed). SEC-2 DNS-resolution path: `localhost` hostname resolves and is rejected.
- **`POST /push-subscription/unsubscribe`** — owner-scoped removal, IDOR (can't remove another user's), idempotent on unknown endpoint; auth.
- **Concurrency / upsert races** (`ConcurrencyUpsertRaceTests`) — DB-level unique index fires for
  `push_subscription.endpoint` and `notification_preference(user_id, type, channel)`; two sequential endpoint calls produce one row (last writer wins).
- **WebPush not configured** (`WebPushNotConfiguredTests`) — empty VAPID keys short-circuit web-push dispatch; SignalR still fires; history row is persisted; subscription not pruned.
- **Email channel** (`EmailChannelTests`) — a default-ON type mails each recipient once with subject = rendered title and the body inside the HTML wrapper (one history row, not two); per-type defaults
  (`ReminderDigest` / `Test` / `UpcomingHrEvents` send nothing, `LeavePending`
  does) and explicit preference rows overriding those defaults **both** ways; unconfigured SMTP short-circuits while SignalR + history are untouched; a recipient with a null address is skipped without
  failing the batch; a throwing mailbox doesn't stop the other recipients or reach the caller; all-three-channels-off ⇒ no history row, while email-only-on still writes one. Plus a
  **parallel-DbContext regression guard** (`EmailAndWebPushBothConfigured_…`): email + Web Push both configured in one `NotifyAsync` — both must dispatch without a "second operation on this context"
  throw, since UserManager and the push read share the scoped context.

- **Quiet-hours window math** (`QuietHoursPolicyTests`, unit, in `HBCleaning.Tests/unit/notification/`) —
  `IsWithin` same-day / overnight-wrap / degenerate (inherited from the retired
  `ReminderQuietHoursPolicyTests`), the `QuietHoursWindow` overload agreeing with the minute overload, and
  `ResumeAt`: null outside the window, the end later today, the overnight evening leg ending tomorrow, the after-midnight leg ending the same day, and **both DST transitions in a real zone**
  (Europe/Bratislava) — spring-forward walks past the wall-clock reading that never happens, fall-back takes the *later* of the two ambiguous instants so the window is never cut short.
- **Quiet-hours endpoints** (`NotificationQuietHoursEndpointTests`) — auth matrix incl. a plain User role managing their own window; GET with no row returns `null`; upsert without duplicating;
  `Start == End`, out-of-range and negative rejected **and nothing persisted**; an overnight `Start > End` accepted; DELETE removes and is idempotent with no row; self-scoping for all three verbs
  (another user's window is neither read, overwritten, nor cleared).
- **Deferral + flush** (`QuietHoursDeferralTests`) — a recipient inside their window gets a history row with
  `DeferredUntil` landing on the window's end wall-clock minute; out-of-window and no-window recipients are immediate; an out-of-window recipient **in the same batch** is not held back by a quiet one;
  an InApp-only recipient is never stamped. Flush: due rows are delivered and cleared, not-yet-due rows are left, a second run is a no-op (and never adds/removes rows), never-deferred rows are
  untouched, and a deferred notification is visible in the bell immediately.
    - Windows are **derived from the deployment time zone at test time**, not hard-coded — a fixed 22:00–06:00 window would pass or fail depending on when CI runs. Keep that if you extend these.
- **Job registrations** (`NotificationsScheduledJobsRegistrarTests`) — the purge's daily 03:15 cron, the flush's `0 0/15 * * * ?` + `DisallowConcurrent`, and a blanket check that every registration
  uses its
  `HandlerKey` as its `JobKey` under `OwnerModule = "Notifications"`. **Select a registration by key** — the list is no longer a single entry.

## Known gaps (living list)

- SignalR hub itself is exercised only through the faked `IHubContext` — no real WebSocket round-trip (by design).
- The flush job's coverage asserts the **state machine** (which rows are picked up and cleared), not that Web Push / Email actually left the box on that path — Web Push is unconfigured in the test
  host, so
  `DispatchDeferredAsync`'s transport fan-out is only indirectly exercised. The live-send equivalents are covered by `EmailChannelTests` / the dispatcher tests. Worth closing if the flush path ever
  grows channel-specific logic of its own.
- No test drives a deferral **across** a DST transition end-to-end (seed at 23:00 the night the clocks change, flush the next morning). `QuietHoursPolicyTests` covers the arithmetic that decides it,
  which is where the risk actually lives.
