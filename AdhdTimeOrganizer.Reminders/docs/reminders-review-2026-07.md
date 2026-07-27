# Reminders — Legal & Product Audit (2026-07)

> Scope: `AdhdTimeOrganizer.Reminders` + its `Kernel.reminders` contract. This is a **delta
> audit** against the phase-01→05b build (see `docs/summary.md`) and the sibling infra audits
> ([Scheduler](../../AdhdTimeOrganizer.Scheduler/docs/scheduler-review-2026-07.md),
> [Notifications](../../AdhdTimeOrganizer.Notifications/docs/notifications-review-2026-07.md)).
> Auditor date: 2026-07-06.

## Verdict

Reminders is a **pure infrastructure module** — like Scheduler, it owns *when* something fires and carries **no seeded Slovak statutory figures, rates, calendars, or quotas**. The statutory numbers
that reach it (the GDPR DSAR "one calendar month", contract deadlines, §63 notice periods, …) are computed **in the owning module** and handed to `RegisterAsync` as an already-resolved instant, so
Reminders pins nothing and correctly registers nothing in `docs/routineLawCheckups.md` (a documented negative — do not re-hunt). **(1) Legal:** the module does not compute a legally-wrong number, so
there is no HIGH finding; the exposure is entirely **GDPR housekeeping** — two append-only ledgers (`reminder_dispatch`, `reminder_occurrence_action`) plus `reminder_definition` grow **unbounded with
no retention/purge and no erasure hook** (Art. 5 (1)(e) storage-limitation; §13 zák. 18/2018), the exact gap flagged in both sibling infra modules. **(2) Complete for the <100-employee segment:** the
engine is feature-complete and well-tested, **but at audit time it had zero live producers** — every candidate owner (GDPR, Zmluvy, Registratura) was wired to a `NoOp…` adapter, so nothing was ever
registered and the scan dispatched nothing. That was the single most important gap: the module was built but **unplugged**. **Partly closed 2026-07-20 — Zmluvy is the first live producer**
(`ReminderContractDeadlineRegistrar`, zmluvy follow-up 01: contract expiry / renewal-notice / CRZ publication + obligation-due deadlines, resolver-strategy recipients, keyed renderers). GDPR and
Registratura remain on their no-ops. **(3) Better:** wire one real adapter end-to-end, then dogfood its own scan for a retention purge and add a failure-alerting seam (shared with Scheduler).

**HIGH findings:** none. The findings below are MEDIUM/LOW GDPR-housekeeping and one carried code-note.

---

## Owner decisions (2026-07) — approved, NOT yet implemented

Recorded from the owner review of Part 4. Nothing below is built in this audit pass.

- **D-Q1 (→ L1 / B2). Centralize retention. ✅ DONE 2026-07-20.** Build one shared ledger-retention mechanism across Scheduler + Notifications + Reminders — shared config shape + shared "older-than-N
  **and** keep-last-N-per-group" delete primitive, each module hosting its own purge job on the Scheduler substrate. Window aligns with `audit_log` (**3 years**, `AuditLogRetentionYears = 3`), NOT the
  10y business horizon, since these are technical/operational ledgers. Build prompt was
  `prompts/reminders-followups/01-centralize-ledger-retention.md`.
  <br>**As built, with one deviation:** the Scheduler and Notifications purges already existed by the time this ran (2026-07-06 and -19), so the work was the Reminders purge + extracting the shared
  policy shape (`RetentionOptions` in `Sydowwe.Framework`) + putting Scheduler on it. The prompt's "one shared delete primitive" was built and reverted the same day — see L1's Done block.
  **Notifications was left on its own day-based policy**, likewise for the reason given there.
- **D-Q3 / D-Q4 / D-Q5 (→ C1 / L2-doc / cron-TZ). Handled in the Scheduler module.** The DST/timezone fix (adding an optional timezone to the schedule/cron surface — Scheduler D3/B4), the
  `[AuditIgnore]` + "no free-text PII in payloads" contract-doc hardening (Scheduler D4), and the ability to **pass a timezone** through the schedule spec are all being solved in
  `AdhdTimeOrganizer.Scheduler/docs/scheduler-review-2026-07.md`. Reminders inherits the cron fix automatically — it evaluates `RecurringCron` through `Kernel.scheduling.ICronEvaluator`, whose only
  implementation is `Core.Scheduler`'s `CronEvaluator`; when the Scheduler work threads a timezone into that surface, Reminders' `ReminderOccurrenceCalculator.NextCron` picks it up. **No
  Reminders-local action** — C1 (Part 3) and the L2 payload-doc hardening are closed here as
  "deferred to Scheduler." The L2 *erasure hook* (nulling recipient ids for an anonymized user) is a separate Reminders question — and it turned out **not** to fold into L1/B2's purge (age ≠ on
  demand). It shipped 2026-07-20 as the Kernel `ISubjectDataEraser` fan-out; see L2.
- **D-Q6 (→ L3). RoPA + DSAR fold-in — owned by the GDPR module.** Add one `ProcessingActivity` (RoPA)
  row for scheduled-reminder dispatch and include a user's reminder recipiency in the DSAR export. Self-contained build prompt: `prompts/reminders-ropa-dsar.md`. Sequenced to land once the first real
  producer (B1) is live so there is actual data to export.
- **D-Q2 (→ B1). Wire real producers.** Both the GDPR adapter and the Zmluvy adapter will be built, **each handed to its own clean-context agent** (separate B1 / B1' tasks) — not sequenced here.

---

## Part 1 — Legal findings

### L1 — `MEDIUM` — Append-only ledgers have no retention/purge (storage-limitation) — **✅ FIXED 2026-07-20**

**Rule.** GDPR Art. 5 (1)(e) (storage limitation) + zák. 18/2018 Z. z. §13; personal data (here the recipient `UserId`s and any subject reference frozen into a dispatch snapshot) must not be kept in
identifiable form longer than necessary. Slov-lex: [zák. 18/2018 §13](https://www.slov-lex.sk/pravne-predpisy/SK/ZZ/2018/18/),
[GDPR Art. 5](https://eur-lex.europa.eu/legal-content/SK/TXT/?uri=CELEX:32016R0679).

**What the code does today.** Three tables grow without bound and nothing ever deletes from them:

- `ReminderDispatch` — append-only ledger, `[NoAudit]`, `Restrict` FKs, `RecipientsSnapshot` jsonb of user ids (`ReminderDispatchEntityConfiguration.cs:16`). Rows are inserted, never removed.
- `ReminderOccurrenceAction` — append-only snooze/dismiss ledger, plain `UserId`
  (`domain/entity/ReminderOccurrenceAction.cs`).
- `ReminderDefinition` — one row per key; `CancelAsync` only flips `Status = Cancelled`
  (`infrastructure/ReminderRegistryService.cs`), it never deletes the row or scrubs `PayloadJson`.

There is **no** purge handler, no retention option, and no scheduled cleanup anywhere in the module (`grep -ri 'purge\|retention' Core.Reminders` → nothing). Ironically the module already *is* a
scheduled-scan consumer of the Scheduler substrate, so it has everything needed to purge itself.

**Consequence.** Over years of operation the dispatch/action ledgers accumulate every recipient id of every reminder ever fired, unreachable by any erasure or minimization routine — the same Art. 5
(1)(e) exposure raised as Scheduler **L1** and Notifications **L2**.

**Fix direction (no migration).** Dogfood the module's own engine: a self-registered
`Reminders.PurgeExpiredDispatchLog` handler (an `IScheduledJobHandler`, mirroring the audit-log purge job) that deletes `ReminderDispatch` / `ReminderOccurrenceAction` rows older than a configured
window and `Cancelled`/`Completed` `ReminderDefinition`s past a grace period. Window as a
`ReminderRetentionOptions` config value (no migration). Coordinate one shared retention policy across Scheduler + Notifications + Reminders rather than three ad-hoc windows.

**Done (2026-07-20, follow-up 01).** `PurgeExpiredReminderLedgersJobHandler` (`application/job/`, key
`Reminders.PurgeExpiredDispatchLog`), self-registered on the Scheduler substrate as the module's *second* recurring job via `RemindersScheduledJobsRegistrar.RetentionPurgeRegistration` — monthly cron
`0 45 3 1 * ?` (offset from Core 03:00 / Notifications 03:15 / Scheduler 03:30), `DisallowConcurrent`. Window from `ReminderRetentionOptions` (section `ReminderRetention`), defaulting to the shared 3
years / keep-last-20-per-definition. Still no migration.

Three passes, ordered by the `Restrict` FKs — this ordering is the whole difficulty of the fix:

1. `reminder_dispatch` first (the only table pointing at the other two), excluding rows another dispatch still reverses (`ReversesDispatchId` self-FK).
2. `reminder_occurrence_action` second, so markers whose fulfilling dispatch just aged out in pass 1 become collectable in the *same* run; excludes rows still referenced by `ReversesActionId` or by a
   surviving `ReminderDispatch.ReminderOccurrenceActionId`.
3. `reminder_definition` last, `Cancelled`/`Completed` only (age = `CompletedAt`, falling back to
   `ModifiedTimestamp` for cancelled rows, which never get one), and only once **both** ledgers are clear of it. `Active`/`Paused` are never eligible regardless of age. This pass is deliberately
   **tracked, not set-based**: `ReminderDefinition` is audited, so the deletion lands in `audit_log` like any other (`PayloadJson` is `[AuditIgnore]`, so no owner-supplied free-text PII enters the
   snapshot). Opt-out via `PurgeTerminalDefinitions = false`.

Passes 1–2 are each one `ExecuteDeleteAsync` in plain LINQ: age gate, keep-last-N-per-definition floor, FK guards. What *is* shared with Scheduler is the policy shape —
`Sydowwe.Framework/…/retention/RetentionOptions.cs`. A shared generic delete helper was built and then reverted the same day: it absorbed only the trivial half into expression-tree machinery that left
the type system, while the FK guards (the hard, per-ledger part) stayed in the callers anyway. Share the policy, not the query. **Notifications deliberately keeps its own policy** (90d read / 365d
all / 180d stale subscriptions, daily): its windows are in days because notification rows carry payload PII, and folding it into the generic 3-year keep-last-N shape would have *loosened* a live GDPR
control. Shared shape ≠ shared values.

Covered by 15 integration tests (`PurgeExpiredReminderLedgersJobHandlerTests`, Postgres) — both floors, all three FK guards, the pass-1-frees-pass-2 ordering, terminal-definition eligibility, the kill
switch and idempotent re-run.

### L2 — `LOW` (conditional) — Erasure blind spot: `UserId` + `PayloadJson` survive employee anonymization — ✅ **DONE 2026-07-20** (recipient-side; `PayloadJson` left as a known residual)

**Rule.** GDPR Art. 17 (erasure) / §23 zák. 18/2018. When an employee is anonymized, personal data about them across all systems must be removed or anonymized.

**What the code does today.** `EmployeeErasureService` + `AnonymizeTerminatedEmployeesJob`
(`Core.EmployeeModule`) anonymize the employee record; **neither touches Reminders**. Reminders holds recipient user ids in four places (`ReminderRecipient.UserId`, `ReminderOccurrenceAction.UserId`,
`ReminderKindPreference.UserId`, `ReminderQuietHours.UserId`) plus the immutable
`RecipientsSnapshot` in the dispatch ledger, and an opaque `ReminderDefinition.PayloadJson` that — per its own XML doc (`domain/entity/ReminderDefinition.cs:41-51`) — *can* carry free-text third-party
PII (names/addresses).

**Why LOW today, not HIGH.** Two mitigations already hold: (a) `PayloadJson` is `[AuditIgnore]`, so it never enters `audit_log` (this correctly avoids the Notifications-L1-style audit-snapshot leak);
(b) the only real producers written so far pass **ids, not PII** — the GDPR module registers with
`SubjectRef(request.Id)` (`DataSubjectRequestService.cs:49,116`), no name/email in the payload. So the leak is **latent**: it activates only if a future owner puts free-text PII in `PayloadJson`, or
if plain recipient ids are themselves considered erasable identifiers. Because a `UserId` is a pseudonymous key and the user row is anonymized in place (id retained), this is closer to Scheduler
**L2** (convention-guarded) than to Notifications' live HIGH.

**Fix direction.** (a) Harden the contract doc: state in `IReminderRegistry` / `ReminderRegistration`
that `Payload` must carry **ids only, never free-text PII** (same rule as the logging redaction policy). (b) When L1's purge lands, have it double as the erasure path, or add a small
`IReminderErasure` hook the `EmployeeErasureService` calls to null out recipient rows / snapshots for an anonymized user id. Decide with the owner whether pseudonymous recipient ids need erasure at
all.

**Done (2026-07-20, follow-up 03).** Fix direction (b), as a Kernel fan-out rather than a Reminders-specific hook. ⚠️ Its "(or have L1's purge double as the erasure path)" half was **wrong** and is
corrected in B2 below: retention deletes by *age*, erasure by *subject, on demand* — different axis, different mechanism. Fix direction (a) shipped separately — see the block below.

**Done (2026-07-21, `prompts/payload-pii-contract.md`).** Fix direction (a), and it went further than
"harden the contract doc": the rule is now **type-enforced, not documented**. `ReminderRegistration.Payload`
is `IReminderPayload` and `RenderedReminder.Payload` is `INotificationPayload` (both Kernel markers), so an owner physically cannot register an anonymous object carrying a name. The rule itself is
stated **once**, in
`Kernel/notification/payload/INotificationPayload.cs`, and `ReminderDefinition.PayloadJson`'s XML doc now points at it instead of conceding it "can carry free-text third-party PII" — that sentence is
gone, because it is no longer true. `PayloadPiiContractGuardTests` (HBCleaning.Tests) reflects over every type implementing either marker across all modules and fails on a person-data property name,
SK + EN.

⚠️ **Two paths stay runtime-enforced, by necessity.** `POST /reminder-definition/register` accepts a payload as arbitrary JSON off the wire — no compiler can constrain that — so the endpoint runs
`PayloadPersonDataNames.ContainsPersonData` over the posted document and returns a validation error naming the offending path. And dispatch rehydrates the *already-persisted* document via
`RawNotificationPayload`; that wrapper is rehydration, not authoring, and is excluded from the guard test on purpose. The contract is enforced where the document is **written**.

The `PayloadJson` residual noted in this L2's heading is therefore narrower than it was: nothing new can be written into it, but rows persisted before 2026-07-21 were never scrubbed, and erasure still
does not reach into payload documents (deliberately — see the enricher's design note).

- **Contract:** `MojaDigitalnaFirma.Kernel/gdpr/ISubjectDataEraser.cs` (+ `SubjectErasureRequest`, carrying **both** `EmployeeId` and `UserId` — this module keys everything by `UserId`, the Employee
  module by `EmployeeId`, and `Employee.UserId` bridges them once at the call site).
  `EmployeeErasureService` resolves `IEnumerable<ISubjectDataEraser>` and fans out, exactly as
  `EmployeeDataExportService` composes `IEmployeePersonalDataProvider` on the read side — **no new cross-module project reference**, and a host shipping neither module gets an empty enumerable.
  Per-module row counts land in the `EmployeeAnonymized` audit payload as `ModuleErasures`, so a module that was never registered shows up as an absent key rather than a silent gap.
- **Failure policy: throw, don't dead-letter.** Unlike `ILeaveAttachmentEraser` (which talks to remote file storage, where a transient outage is expected), these erasers touch only the local DB inside
  a transaction the caller rolls back. A loud failure leaves the subject un-erased and visibly retryable; a silently skipped one is an Art. 17 hole nobody finds until an audit.
- **Implementation:** `Core.Reminders/application/service/ReminderSubjectDataEraser.cs`. Per-table choice, driven by the append-only/`Restrict`-FK ledger invariants:

  | Table | Action | Why |
          |---|---|---|
  | `ReminderRecipient` | **delete** (tracked — the entity is audited) | Cascade-owned by its definition, no ledger invariant. An anonymized person is not a recipient. |
  | `ReminderKindPreference` | **delete** | Per-user settings, meaningless once the subject is gone. |
  | `ReminderDefinition` (left with 0 recipients) | **`Cancelled`** + `IsActive = false` + `NextOccurrenceAt = null` | Otherwise the scan wakes it every occurrence forever only to append a `Skipped`/`NoRecipients` row. `Cancelled` (not deleted) keeps the registry row so a re-register reactivates it, and the L1 purge collects it once terminal. |
  | `ReminderOccurrenceAction` | **pseudonymize** `UserId` → `0` | Append-only evidence; a dispatch may point at it via `ReminderOccurrenceActionId` (`Restrict`), and the reversal lineage must survive. Its `(DefinitionId, OccurrenceAt, UserId)` index is **non-unique** (confirmed in `ReminderOccurrenceActionEntityConfiguration`), so collapsing several erased users onto one sentinel violates nothing. |
  | `ReminderDispatch.RecipientsSnapshot` | **scrub the id out of the jsonb array** | Documented "ids only, no PII", but an id still identifies. Row + array shape kept; candidates narrowed in SQL with `@>` (`EF.Functions.JsonContains`) so the ledger is never materialised. |
  | `ReminderDefinition.PayloadJson` / `.SubjectId` | **untouched — known residual** | Subject-side, not recipient-side (see below). |

- **Erasure is RECIPIENT-SIDE ONLY.** Rows *addressed to* the subject go; rows *about* the subject that belong to someone else stay. Subject-side payload PII is handled by the **render-time**
  `INotificationPayloadEnricher` (notifications follow-up 02) — payloads are minimized to ids and names resolve at read time, precisely so an anonymized employee degrades on its own with no backfill
  and no payload scrubber. A test asserts a notification *about* the erased employee in another user's bell survives with its payload byte-identical, so nobody later "fixes" this into a scrubber.
- **Known residual:** `ReminderDefinition.PayloadJson` is only *convention*-guarded ("ids only"), not minimized by construction like the Notifications payload, and its own XML doc concedes it can
  carry free-text third-party PII. It is opaque and owner-supplied, so erasing it correctly needs the producing module's knowledge of its shape. Tightening it is tracked in
  `prompts/payload-pii-contract.md`, not here.
- **Tests:** `MojaDigitalnaFirma.AdminPortal.Tests/integration/service/gdpr/SubjectDataErasureTests.cs`
  (7, Postgres, driven through the real anonymize endpoint) — registration of both erasers, recipient + preference removal with a co-recipient untouched, orphaned-definition cancellation, ledger
  pseudonymization + snapshot scrub, the Notifications side, the scope guard above, and idempotency (driven through the erasers directly, since the endpoint short-circuits on `IsAnonymized`).
  `ReminderContractGuardTests` / `RemindersNoQuartzGuardTests` unaffected and green.
- Retention windows are unchanged — this is the on-demand axis, so `docs/routineLawCheckups.md` needs nothing.

### L3 — `LOW` (conditional) — No RoPA row, no DSAR-export inclusion — ✅ **DONE 2026-07-20**

**Rule.** GDPR Art. 30 (records of processing) + Art. 15/20 (access/portability). Consistent with the Notifications **L3** finding.

**What the code does today.** Reminders processes personal data (recipient ids, snapshots) but there is no processing-activity (RoPA) entry describing it, and the personal-data export path
(`ExportMyDataEndpoint`, DSAR bundle) does not include a user's reminder recipiency / snooze history.

**Consequence.** A subject-access or RoPA request would omit reminder data. Low impact — the data is sparse and id-only — but it should be catalogued once the GDPR module (`Core.OchranaUdajov`) owns
the RoPA/DSAR surface.

**Fix direction.** Add a RoPA row for "scheduled-reminder dispatch" and, if the owner wants DSAR completeness, a `GetMyUpcomingReminders`-style read into the DSAR export. Defer to the GDPR module's
DSAR-bundling decision (its own Q).

**✅ Shipped 2026-07-20** (`prompts/reminders-followups/02-ropa-dsar-inclusion.md`), after Zmluvy became the first live producer.

- **RoPA (Art. 30).** `ActivityKey = "reminder-dispatch"` / `ZSC-12` in `DevProcessingActivitySeeder`. RoPA is dev-seed-only + admin CRUD in vanilla (there is no production seeder), so the seeded row
  *is*
  the default template the customer's DPO edits. Legitimate interest with a balancing-test note,
  `DataSubjectCategory.Employee`, no special category, retention pointing at the 3-year
  `ReminderRetention` purge (follow-up 01). Category-only — no cross-module reference.
- **DSAR (Art. 15/20).** `ReminderPersonalDataProvider` contributes a `"reminders"` section through the existing `IEmployeePersonalDataProvider` seam: explicit `ReminderRecipient` rows, the subject's
  own
  `ReminderOccurrenceAction` history, and their `ReminderKindPreference` rows. Resolver-strategy reminders are excluded by a `RecipientMode == ExplicitUsers` filter — a read path never invokes an
  `IReminderRecipientResolver`, same rule as `GetMyUpcomingRemindersEndpoint`. `PayloadJson` and co-recipient ids are excluded (third-party PII / other data subjects).
- **Coupling choice — the provider lives in the composition project `AdhdTimeOrganizer`**
  (`infrastructure/service/`), not in `Core.Reminders`. `IEmployeePersonalDataProvider` is an
  `EmployeeModule.Contracts` type, and bridging `employeeId → UserId` needs the `Employee` entity itself — a full domain reference. `AdhdTimeOrganizer` already composes both, so **`Core.Reminders`
  keeps its Kernel + Framework-only reference set** and `EmployeeModule` still never references Reminders. Guard tests re-run green.
- **Quiet hours are out of scope here** — the window moved to Notifications (`NotificationQuietHours`)
  in notifications follow-up 05 and belongs to that module's own (still-parked) L3.
- Tests: `DevProcessingActivitySeederTests`, `ReminderPersonalDataProviderTests`,
  `ExportMyDataEndpointTests.OwnRecord_FoldsInReminderSlice`.

---

## Part 2 — Segment fit (<100-employee Slovak company)

Ranked by expected demand. The engine itself is complete; the gaps are **adoption + operability**.

1. **Wire at least one real producer (blocking — see B1).** Today the module fires nothing because every owner uses a `NoOp` adapter. A <100-person company gets zero value until GDPR deadlines or
   contract deadlines actually flow in. This is the #1 "completeness" gap and it is not a nicety — without it the whole module is dead weight.
2. **Failure visibility / alerting.** A `Failed` dispatch (missing resolver, `NotifyAsync` throw, unresolved text) writes a `Failed` row and moves on — nobody is told, and the only surface is the
   admin dashboard's `FailedNeedingAttention` count (pull-only). For statutory-deadline reminders (DSAR one-month, contract expiry) a silently-failed reminder is a compliance miss. Shared seam with
   Scheduler's identical gap.
3. **Retention/purge (L1).** Also a segment expectation — small customers don't want an ever-growing ledger and have DPA obligations.
4. **Escalation / acknowledge.** "Remind, and if nobody acted by the deadline, escalate to the manager." Currently a reminder fires N times per schedule but has no notion of "was the underlying task
   done?" — that ack lives in the owner. A lightweight escalation ladder (recipient → manager on overdue) is high-value for legal deadlines.
5. **A minimal admin "create ad-hoc reminder" UI.** All registration is code-side (owner modules). A one-off "remind me/these users on date X" admin screen would make the dashboard self-serve rather
   than read-only.

**Deliberate non-goals** (scope discipline): no per-reminder free-text channel routing (belongs in Notifications — see summary's ChannelHint note); no clustering / persistent Quartz store (single-node
monolith — a Scheduler-wide decision, not here); no per-user timezone (the repo models one deployment zone; revisit only if per-user TZ lands globally); no reminder "templates library" UI (text is a
`NotificationType` + renderer, owned by Notifications).

---

## Part 3 — Code-level notes (load-bearing only)

- **C1 (carried from Scheduler C1) — `RecurringCron` is UTC-only → DST drift.** `ReminderDefinition.Cron`
  is documented UTC (`domain/entity/ReminderDefinition.cs:65`) and `ReminderOccurrenceCalculator.NextCron`
  evaluates it via the shared `ICronEvaluator` with no zone. A reminder cron'd for "09:00" fires at 10:00 or 11:00 Bratislava depending on DST and shifts twice a year. **Quiet hours are already
  zone-correct** (`ReminderQuietHoursPolicy` converts to the deployment zone) — so the two halves of the module disagree on time semantics. Low impact for lead-offset/one-shot reminders (absolute
  instants), matters only for `RecurringCron`. Fix rides on the Scheduler C1 decision to add a
  `Europe/Bratislava` option to the cron/schedule surface; do it there once, not here.
- The concurrency design (scan `DisallowConcurrent`, `SaveDispatchAsync` detach+retry vs the admin registry, marker-state dedup for snoozes) is carefully reasoned in `summary.md` and not re-litigated
  here — no finding.
- `PayloadJson` `[AuditIgnore]` is the right call and already in place — noted as a *positive*
  (it pre-empts the Notifications-L1 audit-leak class).

---

## Part 4 — Open questions for the owner

- **Q1 — Retention window.** One shared retention policy across Scheduler/Notifications/Reminders, or per-module? Proposed default: dispatch/action ledgers 12 months, `Cancelled`/`Completed`
  definitions 90 days after completion. (Drives L1.)
- **Q2 — Producer to wire first.** Which real adapter proves the seam end-to-end — GDPR DSAR/breach deadlines (`IGdprDeadlineReminderPort`) or Zmluvy contract deadlines (`IContractDeadlineRegistrar`)?
  (Drives B1.) Recommendation: **GDPR** — the DSAR one-month deadline is the highest-consequence "must not be missed" reminder in the product.
- **Q3 — Failure alerting.** Build the failure-alerting seam here, or wait for the shared Scheduler-level one (B2 in the Scheduler audit)? They are the same seam; recommend doing it once in Scheduler
  and consuming it here.
- **Q4 — Payload PII contract.** Approve hardening the contract doc to "ids only, never free-text PII" in `Payload`, and adding the erasure hook (L2)? Or accept pseudonymous recipient ids as
  non-erasable and close L2 as documented-accepted?
- **Q5 — DST (C1).** Confirm `RecurringCron` staying UTC is acceptable for v1 (defer to the Scheduler timezone decision), or is any customer relying on local-time recurring reminders now?
- **Q6 — DSAR/RoPA (L3).** Fold reminder recipiency into the GDPR module's RoPA + DSAR export, or defer until a customer asks?

**Recommended priority:** Q2 → B1 (wire GDPR adapter) → Q1/L1 (retention purge) → Q3 (alerting) → Q4/L2 (payload contract + erasure) → Q5/C1 → Q6/L3.

---

## Part 5 — Bigger product ideas

### B1 — Wire the first real producer adapter (unplug the NoOps)

**What.** Implement `IReminderRegistry`-backed adapters for the existing owner ports —
`GdprDeadlineReminderPort` (replaces `NoOpGdprDeadlineReminderPort`) and/or
`ContractDeadlineRegistrar` (replaces `NoOpContractDeadlineRegistrar`) — plus each owner's
`IReminderRecipientResolver`/`IReminderRenderer` and host wiring. **Fit:** the seam is fully built (contract, registry, scan, dispatch, dashboard) and the owners already call the ports through NoOp
stubs explicitly *waiting* for this — it's the missing last mile, not new architecture. **Risk:** low; the owner-side call sites and idempotency discipline already exist. Main care is the recipient
resolver reaching owner domain code correctly. **Effort:** ~1 phase per producer.

### B2 — Self-hosted retention purge (dogfood the scan) — **✅ DONE 2026-07-20**

**What.** A `Reminders.PurgeExpiredDispatchLog` `IScheduledJobHandler` + `RemindersScheduledJobsRegistrar`
entry that trims the two ledgers and stale definitions on a cadence. **Fit:** the module already registers a recurring job with Scheduler — this is a second handler on the same substrate; closes L1
and doubles as the L2 erasure path. **Risk:** low; `Restrict` FKs mean purge order matters (dispatch before definition). **Effort:** ~1 phase.

**Shipped** as described — see L1's Done block for the three-pass FK ordering. One scope correction: it does **not** double as the L2 erasure path. Age-based purging cannot reach a specific user's
rows on demand, so L2 needed its own Kernel erasure contract (a TODO pointed at it from the handler's header). Built 2026-07-20 from `prompts/reminders-followups/03-erasure-hook.md` — see L2's Done
block.

### B3 — Failure-alerting / dead-letter seam (shared with Scheduler)

**What.** When a dispatch terminalises `Failed`, raise a notification to an ops/admin recipient (itself a reminder or a direct `NotifyAsync`). **Fit:** turns the pull-only dashboard signal into a
push; critical for statutory-deadline reminders. Best implemented once as the Scheduler-level seam (Scheduler audit B2) and consumed here. **Risk:** avoid a Reminders→Notifications *alerting* loop
that itself fails silently; needs a floor/dead-letter. **Effort:** ~1 phase (shared).

### B4 — Escalation ladder ("remind, then escalate on overdue")

**What.** Extend the contract so a reminder can carry an escalation step (recipient → manager after the deadline passes with no ack). **Fit:** natural on top of the snooze/dismiss ledger already
present; the biggest legal-deadline value-add. **Risk:** requires an "acknowledged/done" signal from the owner (Reminders has no view of task completion) — needs a contract addition. **Effort:** ~2
phases.

### B5 — Ad-hoc admin reminder + surfaced dashboard

**What.** An admin "create a one-off reminder" endpoint/UI over the existing registry, plus shipping the phase-05 dashboard frontend. **Fit:** makes the read surface self-serve. **Risk:** low; guard
against admins hand-registering resolver-strategy keys with no resolver. **Effort:** ~1 phase + frontend.

### Recommended order

| Order    | Idea                                                              | Phases     | Main risk                                                       |
|----------|-------------------------------------------------------------------|------------|-----------------------------------------------------------------|
| 1        | **B1** wire GDPR adapter (first real producer)                    | 1          | recipient resolver into owner domain                            |
| ~~2~~ ✅ | B2 — self-hosted retention purge (**done 2026-07-20**, closes L1) | 1          | `Restrict` FK delete ordering — handled by three ordered passes |
| 3        | **B3** failure-alerting seam (shared w/ Scheduler)                | 1 (shared) | alerting-on-alerting loop                                       |
| 4        | **B1'** wire Zmluvy adapter (second producer)                     | 1          | idempotent re-register on date change                           |
| 5        | **B4** escalation ladder                                          | 2          | needs owner "done/ack" signal (contract add)                    |
| 6        | **B5** ad-hoc admin reminder + dashboard FE                       | 1 + FE     | resolver-key-without-resolver guard                             |
