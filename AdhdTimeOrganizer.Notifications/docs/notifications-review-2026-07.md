# Notifications — Legal & Product Audit (2026-07)

> **Owner decisions (2026-07-06).** Q1 ✅ approved (retention windows as proposed). Q2 → **a + b**
> (retention + payload-minimization convention; no erasure-side scrubber). Q3 ✅ email channel
> (reuse `Sydowwe.Framework` `IEmailSenderService`). Q4 ✅ all bell/preference UX items.
> Q5 ✅ approved (quiet hours existed only in Reminders; the notification-layer build absorbs that
> table — one window, owned by Notifications). Q6 **parked** for later. Nothing implemented yet —
> self-contained build prompts live in **`prompts/notifications-followups/`** (01 retention,
> 02 payload minimization, 03 email, 04 UX, 05 quiet hours); recommended order in its README.

**Verdict.** The module is legally *almost* clean — it pins no statutory figures (nothing for
`routineLawCheckups.md` to watch), its consent posture for Web Push is compliant by construction (§109 ods. 8 zák. 452/2021 — the browser permission + explicit subscribe *is* the demonstrable
consent), and PII discipline in logs is good. The one real legal defect is at the seam with EmployeeModule's GDPR erasure: **employee names and leave-type labels are frozen into
`PayloadJson` of *other users'* notification rows and survive anonymization forever** (L1), with no retention limit to age them out (L2). For the <100-employee Slovak segment the delivery core is
complete and well-tested, but the product around it is thin: no **email channel** (the single most-expected feature for admins who won't install a PWA), no way for the SPA to *read* current
preferences, and no bell-UX basics (unread count, mark-all-read, history beyond 50). Best next moves: a retention + erasure-integration phase (kills L1+L2 together), then the preference/bell UX phase,
then the email channel.

Prior review baseline: the June 2026 code review's findings are all resolved or accepted — MIG-1 (missing migration) is **resolved** (notification tables are in both hosts' `Initial`
migrations, e.g. `MojaDigitalnaFirma.AdminPortal.Sandbox/infrastructure/persistence/Migrations/20260705211523_Initial.cs`); SEC-1 (push re-ownership hijack) is **mitigated** — re-own now audited via
`IAuditService.LogAsync("PushSubscriptionReowned", …)`; SEC-2 (SSRF via private-resolving hostname) is **implemented** with the DNS-resolution guard + documented TOCTOU residual (domain-map
§Endpoints); CQ-1 (renderer error isolation) was **redesigned** — render failure is now logged and the whole dispatch skipped (see Part 3, N1). I agree with all four dispositions.

---

## Part 1 — Legal findings

### L1 — HIGH · GDPR Art. 17 / Art. 5 (1)(e): employee PII in notification payloads survives anonymization — ✅ DONE (2026-07-19, Q2 a+b shape)

> **Resolved by payload minimization** (follow-up `02-payload-minimization-employee-id.md`, fix
> direction 2 below; direction 1 / L2 retention is follow-up 01, direction 3 declined by the owner).
> All three call sites now persist `employeeId` instead of `employeeName`. The display name is
> resolved per render by the new `INotificationPayloadEnricher` `Sydowwe.Framework.Contracts` seam
> (`EmployeeNamePayloadEnricher` → `GetEmployeeSummariesCommand`, one dispatch per batch, no-op
> fallback `NoOpNotificationPayloadEnricher` via `TryAddScoped`), applied in
> `NotificationService.NotifyAsync` and `GetMyNotificationsEndpoint` — the enriched JSON is a render
> input only and is never written back. An anonymized employee therefore degrades on its own.
> **Residual (accepted):** rows written *before* this change still hold names; no backfill — the
> retention purge (L2 / follow-up 01) ages them out. Covered by
> `NotificationPayloadEnrichmentTests` + payload assertions in `AttendanceNotificationTests`; see
> `domain-map.md` §"Payload minimization".

**The rule.** GDPR Art. 17 (right to erasure) + Art. 5 (1)(e) (storage limitation); for leave-type labels that reveal sickness (PN, OČR) also Art. 9 (health data — lawful to process for employment
purposes under Art. 9 (2)(b), but not to retain indefinitely). The employer's own retention design (`AnonymizeTerminatedEmployeesJob`, 10y window) is the yardstick the system sets for itself.

**What the code does today.**

- `AddLeaveEndpoint.cs:196` serializes `new { employeeName = employeeSummary?.FullName, leaveTypeName, start, end }`
  into the `LeavePending` payload; `ClockOutWorkLogEndpoint.cs:113` and `AddWorkLogEndpoint.cs:86`
  do the same with `employeeName` + violation text for `WorkLogComplianceBreach`.
- `NotificationService.NotifyAsync` persists that JSON verbatim per recipient (`infrastructure/NotificationService.cs:70`) — these are rows owned by the **HR/Admin/manager recipients**, not by the
  employee the data is about.
- `AnonymizeTerminatedEmployeesJob` → `IEmployeeErasureService.ErasePersonalDataAsync` never touches `Notification.PayloadJson` (verified: no reference to notifications anywhere in the erasure path).
  The `Notification.UserId → User` cascade (`NotificationEntityConfiguration.cs:21`) deletes a *recipient's* rows when the recipient user is deleted — it does nothing for rows *about* a third person.

**Consequence.** After the retention window elapses the job sets `IsAnonymized = true` and its comment claims "irreversibly anonymizes" — but "Zamestnanec Ján Novák podal žiadosť o neprítomnosť (PN)"
still sits, forever, in every HR user's notification history. The system silently records a legally false state (erasure marked complete while personal data remains), which is why this is HIGH despite
no number being computed.

**Fix direction (no migration needed for any of these).**

1. **Retention pruning (see L2)** — with any sane retention window (≤ 12 months) no payload survives to the 10-year anonymization horizon, mooting the erasure gap for history rows.
2. **Payload minimization for new/changed types** — store `employeeId` and resolve the name at render time (renderer already degrades gracefully to the name-less variants when the property is absent,
   `NotificationTextRenderer.cs:112–117`). Post-anonymization renders would then show the anonymized name naturally.
3. Belt-and-braces: teach `IEmployeeErasureService` to null the `employeeName` key in
   `PayloadJson` for the erased employee (single jsonb UPDATE).

Recommended: 1 now + 2 as the convention for future types; 3 only if the owner wants erasure to be airtight independent of retention.

**Sources.** [GDPR Art. 5, 9, 17 (consolidated, EUR-Lex)](https://eur-lex.europa.eu/legal-content/SK/TXT/?uri=CELEX%3A32016R0679); retention yardstick is the module's own
`AnonymizeTerminatedEmployeesJob.cs:19–21`.

### L2 — MEDIUM · GDPR Art. 5 (1)(e): no retention limit on notification history — ✅ DONE (2026-07-19, follow-up 01)

> **Fixed.** `PurgeExpiredNotificationHistoryJobHandler`
> (`application/job/`, handler key `Notifications.PurgeExpiredHistory`) deletes read notifications
> past **90d**, all notifications past **365d**, and push subscriptions idle past **180d**
> (`ModifiedTimestamp` as last-seen). Registered daily at 03:15 UTC by
> `NotificationsScheduledJobsRegistrar` (`infrastructure/scheduling/`), wired once in the shared
> `AddCore`; Quartz-less hosts no-op. Windows are owner policy (2026-07-06), not statutory — they
> are deliberately **not** in `docs/routineLawCheckups.md`.
>
> Prerequisite shipped with it: `SubscribePushEndpoint.ApplyUpdateAsync` now force-touches
> `ModifiedTimestamp` on every re-POST, so the SPA's identical start-up re-subscribe keeps a live
> device's last-seen fresh instead of freezing it at row creation.
>
> **Residual on L1:** L1 was independently closed by follow-up 02 (payload minimization —
> `INotificationPayloadEnricher`), so payloads no longer persist names. For any payload PII that
> predates that fix, or that a future type reintroduces, exposure is now **windowed at 12 months**
> rather than permanent. `NotificationPreference` is never purged — settings, not history.
> Covered by `PurgeExpiredNotificationHistoryJobHandlerTests` (9 tests).
>
> **Reviewed again 2026-07-20 (reminders follow-up 01, "centralize ledger retention") — kept as-is.**
> That task centralized the retention *policy shape* across Scheduler + Reminders
> (`framework/Sydowwe.Framework/…/retention/RetentionOptions.cs`) and proposed converting this handler to the
> same "3 years, keep last N per `UserId`" shape. **Deliberately not done.** These windows are in *days*
> precisely because notification rows carry payload PII, so adopting the generic 3-year keep-last-N shape
> would have loosened a live GDPR control from 90 days to 3 years and overridden the 2026-07-06 owner
> decision. The shared *shape* is not a mandate to share *values*, and this ledger's rules
> (read-vs-unread, stale subscriptions) do not decompose into age + keep-last-N anyway. If this handler
> is ever refactored onto the primitive, keep the day-based windows.


**The rule.** Storage limitation — personal data kept no longer than necessary for the purpose. The purpose here is the bell list + short-term history; the UI never reads past the newest 50
(`GetMyNotificationsEndpoint.cs:28`).

**What the code does today.** Nothing ever deletes `Notification` rows (repo-wide search: only the recipient-cascade and test helpers touch them). Rows — including L1's payloads — accumulate for the
lifetime of the database. `PushSubscription` rows are pruned only lazily on a `404/410`
push response (`NotificationService.cs:154`), so a subscription of a device that never gets pushed to again (e.g. all types opted out) also lingers.

**Consequence.** Unbounded PII accumulation with no purpose to justify it; also the substrate that turns L1 from a windowed exposure into a permanent one. A supervisory authority reviewing retention
(Art. 30 record vs reality) would flag it.

**Fix direction.** One recurring Scheduler-module job (the substrate exists precisely for this):
e.g. delete read notifications older than 90 days and all notifications older than 12 months (owner picks the window, Q1); optionally prune `PushSubscription` rows not successfully pushed to in N
months. Plain `DeleteRangeAsync` in chunks; entities are `[NoAudit]` so no audit-log noise. No migration.

**Sources.** [GDPR Art. 5 (1)(e)](https://eur-lex.europa.eu/legal-content/SK/TXT/?uri=CELEX%3A32016R0679);
[Úrad na ochranu osobných údajov SR — základné zásady](https://dataprotection.gov.sk/uoou/).

### L2b — GDPR Art. 17: no *on-demand* erasure of a subject's own notification rows — ✅ DONE (2026-07-20, reminders follow-up 03)

**The gap L1 and L2 both left open.** L1 fixed what goes *into* a payload (subject-side, minimized to ids). L2 windows history by **age**. Neither reaches the rows *addressed to* an employee who is
anonymized **today** — their bell history and read state, their `NotificationPreference` rows, their
`NotificationQuietHours` window, and their `PushSubscription` rows (a device endpoint plus the client's crypto keys). Those are last week's rows; no age-based purge touches them for months. Unlike the
Reminders half of that follow-up, this one was a **live** gap — the module has produced real per-user data since it shipped.

**No cascade did this for us — verified.** `UserDeactivationService.DeactivateUserAsync` only sets
`IsActive = false` and rotates the security stamp; the `CoreUser` row is deliberately never deleted (`ReactivateUserAsync` needs it for the rehire/boomerang flow). So the FK-backed entities
(`NotificationPreference` / `PushSubscription`, both `BaseEntityWithCoreUser`) were never cascade-collected, and the FK-less ones (`Notification.UserId`, `NotificationQuietHours.UserId`) simply
lingered.

**Fixed** by `application/service/NotificationSubjectDataEraser.cs`, an implementation of the new `Sydowwe.Framework.Contracts`
`ISubjectDataEraser` fan-out (`Sydowwe.Framework.Contracts/gdpr/`) that `EmployeeErasureService` composes as `IEnumerable<ISubjectDataEraser>` — **no cross-module project reference**, the same inversion
`IEmployeePersonalDataProvider` uses on the read side. All four tables are **deleted** (not pseudonymized, as the Reminders side does): none is an append-only ledger — `Notification` is plain bell
history with no dedup invariant or reversal lineage, and the other three are pure user settings. Tracked
`RemoveRange`, not `ExecuteDeleteAsync`, because the caller owns the transaction. Failure policy is **throw** — see the contract's XML doc.

**Scope: recipient-side only (`WHERE UserId = <erased user>`).** A notification *about* the erased employee sitting in a **manager's** bell is that manager's row and is **not** touched — its payload
degrades through the render-time `INotificationPayloadEnricher` (L1's fix), by design. A test asserts that survivor's payload is byte-identical after an erasure, so nobody later "fixes" this into the
payload scrubber the owner explicitly declined as Q2 (c).

Covered by `MojaDigitalnaFirma.AdminPortal.Tests/integration/service/gdpr/SubjectDataErasureTests.cs`
(7 tests, Postgres, driven through the real anonymize endpoint). Retention windows unchanged — this is the on-demand axis, so `docs/routineLawCheckups.md` needs nothing.

### L3 — LOW · GDPR Art. 15: subject-access export omits notification data; no RoPA activity

`ExportMyDataEndpoint` (EmployeeModule) exports the employee's own data but includes nothing from this module — yet a user's notification history, push subscriptions (endpoint URL + device
`UserAgent`), and preferences are all personal data about them. Manual workaround exists (DSAR is a governance-tracked manual process in Core.OchranaUdajov), hence LOW. Same phase should add a
notification-processing row to the RoPA guidance/dev seeder (`DevProcessingActivitySeeder` has no such activity), so customer RoPAs don't silently omit it. Legal basis to record: Art. 6 (1)(f)
legitimate interest (operational workplace notifications); retention: the L2 window.

### L4 — LOW (conditional) · §109 ods. 8 / §116 zák. 452/2021: compliant today, guardrail needed for marketing

Storing the push subscription on the terminal equipment side requires demonstrable consent — delivered by construction: the browser's Notification permission prompt + the user-initiated
`PushManager.subscribe()` POST. Operational employer notifications rest on legitimate interest; **default-on** preferences (absence-means-enabled, `NotificationService.cs:185`) are fine for that. The
conditional: if a customer ever adds a *marketing/promotional* `NotificationType`, default-on breaches §116 (nevyžiadaná komunikácia — prior consent required). No code change now; record the
constraint in `summary.md` gotchas so a future type addition trips over it.

**Sources.** [Zákon 452/2021 Z. z. (aktuálne znenie)](https://www.zakonypreludi.sk/zz/2021-452);
[HASFIRST — zmeny v cookies podľa 452/2021 (§109 ods. 8 výklad)](https://hasfirst.sk/zmeny-v-suboroch-cookies-podla-noveho-zakona-452-2021-z-z-o-elektronickych-komunukaciach/).

### L5 — LOW (conditional) · §52 ods. 10 Zákonníka práce: right to disconnect vs 24/7 push — ✅ CLOSED (follow-up 05)

> **Closed 2026-07-20** by `prompts/notifications-followups/05-quiet-hours-generalization.md`, which also
> delivers **B3**'s quiet-hours half. Per-user `NotificationQuietHours` (one window per user, deployment-wide,
> **opt-in** — no seeded default, since this is posture and not obligation) is enforced at the dispatcher:
> inside the window Web Push + Email are **deferred** via `Notification.DeferredUntil` and delivered by the
> `Notifications.FlushDeferredNotifications` job once it ends. InApp and the history row are never deferred.
> The Reminders module dropped its parallel `ReminderQuietHours` table and now reads the same window through
> the `Sydowwe.Framework.Contracts` `IQuietHoursReader` seam. See `summary.md` §Quiet hours.



Employees on domácka práca/telepráca have the right not to use work equipment during daily/weekly rest, vacation, and holidays, and the employer may not treat non-reaction as a breach of discipline.
Delivering a push at 23:00 is not itself a violation (receiving ≠ obliged to react), but a **quiet-hours / DND window** is the defensive product posture and a real differentiator for the segment (ties
to B3). No finding against current code; conscious-scope row.

**Sources.** [epi.sk — Domácka práca a telepráca §52 ZP](https://www.epi.sk/odborny-clanok/iv-domacka-praca-a-telepraca-52-zp.htm);
[pracovnepravo.sk — právo zamestnanca na odpojenie](https://www.pracovnepravo.sk/sk/casopis/bezpecnost-prace-v-praxi/pracovny-cas-a-pravo-zamestnanca-na-odpojenie.m-1485.html).

### Registry note (`routineLawCheckups.md`)

This module hard-codes **no drifting statutory figure** — nothing to register. The only quasi-legal data are the sk-SK strings in `NotificationTextRenderer` (no legal content) and the retention window
to be chosen in Q1 (once chosen, it belongs in the module docs, not the checkup registry — it's an owner policy, not a statutory number).

### Accepted gaps (delta from June 2026 review)

| Item                              | Status                                                                                  | Position                                     |
|-----------------------------------|-----------------------------------------------------------------------------------------|----------------------------------------------|
| MIG-1 missing migration           | Resolved — tables in both `Initial` migrations                                          | agree                                        |
| SEC-1 push re-ownership           | Mitigated — audited transfer, threat documented                                         | agree; endpoint-URL-as-sensitive note stands |
| SEC-2 SSRF hostname guard         | Implemented — DNS-resolving guard + tests; TOCTOU residual documented (egress firewall) | agree                                        |
| CQ-1 renderer isolation           | Redesigned — swallow + skip whole batch                                                 | agree, with N1 caveat below                  |
| No real WebSocket round-trip test | By design (faked `IHubContext`)                                                         | agree                                        |

---

## Part 2 — Segment fit (<100-employee Slovak company)

Ranked by expected customer demand:

1. **Email channel.** The hard gap. Approvers in a 20-person firm will not all install a PWA; iOS requires it for Web Push at all. Leave-pending and compliance-breach events that reach no connected
   device today reach *no one* until the next login. The `NotificationChannel` enum +
   `IWebPushSender`-style seam were built for exactly this extension. (→ B1)
2. **Preference management is write-only.** There is a `PUT /notification-preference` but **no GET** — the SPA cannot render the user's current opt-outs, so the settings screen either lies or doesn't
   exist. Also no catalog endpoint (which types/channels exist). Practically, the opt-out feature is unusable end-to-end. (→ B2)
3. **Bell-UX basics.** No unread count endpoint (client counts within the 50), no mark-all-read, no pagination past 50, no user-initiated delete. Small-company users notice these within a week. (→ B2)
4. **Quiet hours / DND + digest batching.** Legal tailwind from L5; also plain politeness — HR events at 06:00 Sunday erode trust in the channel. (→ B3)
5. **Per-user localization.** The seam is documented (replace the singleton renderer); for a Slovak-only segment, **deliberately defer**.

**Deliberate non-goals** (state them to keep scope honest): native FCM/APNs (PWA-first decision stands; enum reserves room), SMS (cost/consent burden, no demand signal), Slack/Teams webhooks (segment
mostly doesn't run them), marketing-campaign tooling (would flip L4 from conditional to active), per-notification read-receipt analytics (creepy for the size class, GDPR-negative), and a SignalR
backplane/Redis (single-node deployment model — clustering is out by architecture).

---

## Part 3 — Code-level notes (load-bearing only)

- **N1 — batch-level render abort.** `NotifyAsync` renders once per batch (`NotificationService.cs:57–66`); a poison payload silently drops the notification for **all**
  recipients (logged, no rethrow). Acceptable for v1, but it also structurally blocks per-user localization (one render per batch ≠ one per recipient culture). If B1/локализация lands, restructure to
  per-recipient render with per-recipient catch.
- **N2 — read-side render fragility is fine.** `GetMyNotificationsEndpoint` re-renders stored payloads; `ParseRoot`/`TryGet*` swallow malformed JSON and the switch has a safe default, so a bad row
  cannot 500 the bell. Verified, no action.
- **N3 — push keys stored plaintext.** `PushSubscription.P256dh`/`Auth` are opaque but together with `Endpoint` allow sending push to the device. Rows are only ever fetched by `UserId`, never filtered
  by key — so `EncryptedColumn` is applicable if desired. LOW hardening, owner's call; Web Push payloads themselves are RFC 8291-encrypted in transit (mitigation credit).
- **N4 — recipient existence.** `NotificationRecipients.Users(ids)` inserts rows without verifying the ids exist; one bogus id FK-fails the whole batch `SaveChanges`. All current callers pass resolved
  ids; keep the invariant in mind for future callers.

---

## Part 4 — Open questions for the owner

- **Q1 — Retention window (unblocks L2, most of L1).** Propose: delete *read* notifications
  > 90 days, *all* notifications > 12 months, and push subscriptions with no successful delivery in 6 months — via a Scheduler-module job. Approve the numbers or set different ones?
- **Q2 — L1 fix shape.** (a) retention-only (Q1 alone — simplest, windowed residual exposure), (b) also adopt payload-minimization convention for employee-referencing types (store
  `employeeId`, render-time name lookup), (c) also scrub `employeeName` in payloads inside
  `IEmployeeErasureService` (airtight). Recommendation: **a + b**; c only if the lawyer wants erasure independent of retention.
- **Q3 — Email channel (B1).** The top segment gap. Greenlight for the next build phase, or does the customer pipeline say otherwise?
- **Q4 — Bell/preferences UX phase (B2).** GET preferences + type catalog + unread count + mark-all-read + history pagination. One phase, no legal content. Now or after B1?
- **Q5 — Quiet hours / digest (B3).** Build the DND window (per-user, default 21:00–07:00 + weekends for WebPush only, InApp unaffected), or defer until a customer asks? (L5 makes it defensible, not
  mandatory.)
- **Q6 — DSAR export completeness (L3).** Fold notification history/subscriptions/preferences into `ExportMyDataEndpoint` + add the RoPA template row — bundle into the same phase as Q1's job, or park
  as LOW?

**Priority recommendation:** Q1+Q2 (a+b)+Q6 as one legal-hygiene phase now; then Q4; then Q3; Q5 opportunistically.

---

## Part 5 — Bigger product ideas

### B1 — Email channel — ✅ DONE 2026-07-19

Add `NotificationChannel.Email` + `IEmailSender` (SMTP, per-customer config like
`PushNotificationOptions`), a branch in the dispatcher, and per-type default routing (e.g. approvals → email on by default, digests off). **Fit:** the enum/seam was designed for it; preferences
already model per-channel opt-out. **Risk:** SMTP deliverability/config support per customer deployment; HTML template maintenance. **Effort:** 1–2 phases.

**Shipped** (follow-up 03, no migration — the channel enum is stored as a string):
`NotificationChannel.Email` in `Sydowwe.Framework.Contracts`; `INotificationEmailSender` +
`SmtpNotificationEmailSender` wrapping the framework's existing `IEmailSenderService`
(so no new SMTP secrets — a deployment that already mails password resets is done);
`EmailNotificationOptions` with an `IsConfigured` guard that auto-detects the `MAIL_*` env vars and short-circuits exactly like unconfigured Web Push; a third parallel dispatcher branch with bounded
concurrency (4) and per-recipient try/catch, logging `{UserId}` only — never an address; `NotificationEmailBody` inline-styled HTML wrapper. Preferences moved from absence-means-enabled to
absence-means-`NotificationChannelDefaults`, which keeps InApp/WebPush all-on and makes Email per-type (approvals/compliance/deadlines on, digests + `Test` + unknown types off). The all-channels-off ⇒
no-history-row rule now covers all three. **Legal guardrail (L4):** operational notifications only — a future marketing type must be opt-IN per §116 zák. 452/2021 and must never be added to the
default-on matrix. Covered by `EmailChannelTests`. Follow-up 04 (preferences UX) must surface Email + these defaults.

### B2 — Notification center & preference surface ✅ done 2026-07-20

GET preferences + type/channel catalog, unread-count, mark-all-read, paginated history page, delete-mine. Pure endpoint work on existing entities. **Fit:** closes the write-only preference hole; all
`Base*` endpoint patterns apply. **Risk:** none notable. **Effort:** 1 phase. Shipped as `GET /notification-preference/mine` (full matrix, doubles as the catalog),
`GET /notification/unread-count`, `POST /notification/read-all`, `DELETE /notification/{id}`, and `beforeId`/`limit` keyset pagination on `GET /notification/mine`. See `domain-map.md`
§Endpoints and `testing.md`.

### B3 — Quiet hours + digest policy engine — ✅ quiet-hours half SHIPPED (follow-up 05)

Per-user DND window suppressing WebPush (queue → deliver at window end or fold into a morning digest), reusing the Reminders digest machinery. **Fit:** L5 tailwind; Scheduler + Reminders already
provide the substrate. **Risk:** interaction with Reminders' own digests (two digest concepts must not double-send); delivery-time state adds a table (migration). **Effort:** 1–2 phases.

> **Shipped 2026-07-20 (quiet hours).** Delivered as *queue → deliver at window end*, not as a fold-into-digest:
> deferred rows carry `Notification.DeferredUntil` (a column on the existing table, not a new one) and the
> 15-minute `Notifications.FlushDeferredNotifications` sweep releases them. The double-send risk was avoided
> by **not** introducing a second digest concept here — Reminders keeps its own digest batching, and the
> notification layer only delays background delivery. **Still open:** the morning-digest variant (fold a
> night's deferred notifications into one message instead of releasing them individually), which is the
> genuinely digest-shaped half of B3.

### B4 — Retention & erasure integration (the legal phase)

Q1 pruning job + Q2 (b) payload-minimization convention + Q6 DSAR export/RoPA row. **Fit:**
Scheduler exists; erasure seam exists. **Risk:** none — deletes are chunked, `[NoAudit]`. **Effort:** 1 phase.

### B5 — Delivery-ops dashboard

Failed-push/SignalR metrics, stale-subscription counts, manual resend. **Fit:** mirrors the Integrácie dashboard+retry pattern. **Risk:** building ops UI nobody at this size class opens. **Effort:** 1
phase. **Recommendation: don't build** until a support ticket proves demand.

### Recommended order

| Order | Item                                | Phases | Main risk                               |
|-------|-------------------------------------|--------|-----------------------------------------|
| 1     | B4 legal phase (Q1/Q2/Q6)           | 1      | none — deletes + convention             |
| 2     | B2 notification center/prefs UX     | 1      | none notable                            |
| 3     | B1 email channel ✅ done 2026-07-19 | 1–2    | SMTP config/deliverability per customer |
| 4     | B3 quiet hours + digest             | 1–2    | overlap with Reminders digests          |
| 5     | B5 ops dashboard                    | 1      | low demand — build only on proven need  |