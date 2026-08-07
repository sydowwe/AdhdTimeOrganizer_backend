# Scheduler — Legal & Product Audit (2026-07)

> Delta audit of `AdhdTimeOrganizer.Scheduler` against (1) current Slovak law, (2) segment
> completeness for a single-node modular monolith serving Slovak companies < ~100 employees, and
> (3) product opportunities. Companion to the code-review pass; scope is legal + product, not code style.

## Verdict

Scheduler is a **pure infrastructure module** — a generic time substrate that owns *when* recurring work fires and *how to observe it*, never job bodies or domain data. It therefore makes **no domain
legal claims**: no seeded statutory figures, no rates, no calendars, no quotas. Phase A's usual figure-drift hunt comes up empty, and *correctly so* — the module pins nothing in
`routineLawCheckups.md` and has nothing to pin. Its only legal exposure is **transverse GDPR**: the append-only `scheduled_job_run` ledger has **no retention limit and no erasure reach** (Art. 5 (1)
(e)
storage limitation / Art. 17), which is both a compliance gap and — more sharply — a straight operational bug (unbounded growth, ironic in the one module that migrates the *audit-log* purge job but
purges nothing of its own). One genuine **correctness** concern exists: every schedule is translated `InTimeZone(UTC)`, so any job an owner intends to anchor to Slovak local wall-clock time
(calendar-boundary rollovers, "run at 03:00") silently drifts by the DST offset. For the target segment the substrate is, if anything, over-built (replay, health, exports, per-key gate all shipped);
the one thing a small company with no ops team is missing is **failure alerting** — a recurring job that starts failing (payroll export, anonymization) sits `LastOutcome=Failed` and nobody is told,
because the health view is pull-only. And the substrate is not yet authoritative: **7 of 8 legacy jobs (phase 05) are still hand-wired** outside the registry, so the dashboard under-reports live work.

---

## Owner decisions (2026-07) — approved

Recorded from the owner review of Part 4; the build briefs live in `prompts/scheduler-followups/`. **Status 2026-07-06: D1, D3, D4 are IMPLEMENTED** (follow-ups 03, 01, 02 — see the per-item notes
below and the FIXED blocks in L1/L2/C1/B4). **Still open: D2 + D5** (follow-ups 05/04) **and D6** (phase-05 migration, owner-driven).

- **D1 (→ Q1, L1 / B3).** Build the self-hosted run-log purge. **Retention = 3 years**, matching
  `audit_log` (`PurgeExpiredAuditLogsJobHandler.AuditLogRetentionYears = 3`;
  `AdhdTimeOrganizer/application/job/PurgeExpiredAuditLogsJobHandler.cs:35`). *Not* the 10-year business-audit horizon — the run log is technical/operational, the same class of data as `audit_log`,
  not a business record. **Plus a keep-last-N-per-job floor** so a job that fires only yearly never loses its recent history to the age cutoff (delete a row only if it is BOTH older than 3y AND not
  among the job's most recent N runs; N ≈ 20 is a sane default). *(✅ Implemented 2026-07-06 — see L1's Done block.)*
- **D2 (→ Q2, B2).** Failure alerting is **on by default for every job**, opt-out per job. There is **no
  "job importance" concept today** — none exists on `ScheduledJob` / `RecurringJobRegistration`, so this is new. Simplest modeling: a `SuppressFailureAlert` (or `AlertOnFailure = true` default) bool
  on
  `RecurringJobRegistration` + the entity, so a non-critical job can silence itself. (An `Importance`
  enum is an option if other features later want it, but a single bool covers this need — don't over-model.) **Alert fires only after the last retry fails** (see D5), not on the first transient
  failure — so alerting depends on the retry feature landing.
- **D3 (→ Q3, C1 / B4).** Timezone bug confirmed — **owner will implement in a dedicated clean-context agent.** This file only needs to describe it precisely (see C1 + B4); do **not** implement here.
  *(✅ Implemented 2026-07-06 — see C1/B4's FIXED blocks; default `null ⇒ UTC`, opt-in `"Europe/Bratislava"`.)*
- **D4 (→ Q4, L2).** Approved: add `[AuditIgnore]` to `ScheduledJob.PayloadJson` + harden the no-PII rule into the `IScheduledJobHandler` / `RecurringJobRegistration` contract doc. Documented in L2 as
  the fix; implementation deferred. *(Recheck 2026-07-06: the code edits already sit in the uncommitted working tree — `ScheduledJob.cs`, `RecurringJobRegistration.cs`, `ScheduledJobContext.cs`.
  Follow-up 02 is now verify + build + doc-update only.)*
- **D5 (→ Q5, B5).** Build **auto-retry** and **one-shot `ScheduleSpec`**. Retry decisions:
  **count is settable per-job** (a field on `RecurringJobRegistration`) with a sensible default (**3**), and **backoff is incremental** (each retry waits longer than the last — e.g. exponential
  `base × 2^attempt`, so ~ 1 min → 2 min → 4 min) to give a struggling dependency time to recover. One-shot = a `RunOnceAt(datetime)` spec (small addition).
- **D6 (→ Q6, B1).** Finish phase-05 migration, **one owning module per session** to keep context focused. Owner drives it; do C1/timezone (D3) first because two of the migration targets are the
  attendance calendar-boundary jobs C1 protects.

> **Standing instruction:** these are all *documented decisions* for later build sessions. This audit
> pass implements none of them.

---

## Part 1 — Legal findings

### L1 — `scheduled_job_run` has no retention / purge — unbounded ledger, unreachable by erasure — **MEDIUM — ✅ FIXED 2026-07-06**

- **Rule.** GDPR Art. 5 (1)(e) (storage limitation — personal data kept "no longer than necessary")
  and Art. 17 (erasure). Slovak transposition: zák. **18/2018 Z. z.** o ochrane osobných údajov, §13 ods. 1 písm. e) and the right-to-erasure obligations.
  ([slov-lex 18/2018](https://www.slov-lex.sk/ezbierky/pravne-predpisy/SK/ZZ/2018/18/),
  [UOOU SR – zásady spracúvania](https://dataprotection.gov.sk/sk/))
- **What the code does today.** `ScheduledJobRun` is append-only by design — one INSERT per run, *never* deleted (`ScheduledJobRunEntityConfiguration.cs` sets both FKs to `DeleteBehavior.Restrict`;
  `domain/entity/ScheduledJobRun.cs:9-20`). There is **no purge handler and no retention policy** for it anywhere in the module (`grep` for `Purge|retention|olderThan|DeleteRange` in
  `Core.Scheduler` → nothing). Rows accumulate for the life of the deployment. The module *does* own the recipe for retention — it migrated `Core.PurgeExpiredAuditLogsJob` onto itself
  (summary §"Worked example") — but never applied that pattern to its own ledger.
- **Consequence.** (a) The run log grows without bound; the Admin dashboard grids (`GetJobRunHistoryEndpoint`, exports that "materialize the whole filtered set in memory", summary l.200) degrade over
  years. (b) Any personal data that reaches a run row via
  `PayloadSnapshotJson` (see L2) or `ErrorMessage` outlives every retention basis and cannot be reached by a DSAR-driven erasure — the same failure mode already flagged HIGH in the Notifications and
  Reminders audits (`[[project-notifications-module]]`, `[[project-reminders-module]]`), here left MEDIUM only because current payloads are static registration config, not per-subject data.
- **Fix direction (no migration).** Dogfood the substrate: ship a `Scheduler.PurgeExpiredRunLogs`
  `IScheduledJobHandler` + a `SchedulerScheduledJobsRegistrar` (mirroring `CoreScheduledJobsRegistrar`), monthly, deleting `scheduled_job_run` rows older than **3 years** (owner decision D1 — matches
  `audit_log`'s `AuditLogRetentionYears = 3`, the same technical-data class, not the 10y business-audit horizon), **AND** keeping the last **N ≈ 20** runs per job so a yearly job never loses recent
  history to the age cutoff (delete iff older-than-3y **and** not among the job's most-recent-N). Deletion of a ledger row is acceptable here *because* the run log is `[NoAudit]` self-audit, not a
  legal record of a business event — but wire it as a hard delete on `ScheduledJobRun` only, never touching `ScheduledJob`. Register a
  `routineLawCheckups.md`-adjacent note only if the window is ever tied to a statutory period (it isn't today — it's an operational default).
- **Done (2026-07-06).** `PurgeExpiredRunLogsJobHandler` (`application/job/`, key
  `Scheduler.PurgeExpiredRunLogs`, `RetentionYears = 3`, `KeepLastRunsPerJob = 20`) self-hosted via
  `SchedulerScheduledJobsRegistrar` (`infrastructure/scheduling/`), monthly cron `0 30 3 1 * ?` (offset from the audit-log purge at 03:00), `DisallowConcurrent`, wired in
  `HbCleaningServiceExtensions`. One rule was **added** beyond the original fix direction: the delete set also excludes any row referenced by another row's `ReplaysRunId` — the replay-lineage self-FK
  is `DeleteBehavior.Restrict`, so a naive age+floor delete would abort with an FK violation the first time an expired run had been replayed; excluded lineage tails age out over successive monthly
  runs instead. Covered by 6 integration tests (`PurgeExpiredRunLogsJobHandlerTests`), including the FK-lineage chain and idempotent re-run. `ExecuteDeleteAsync` is used deliberately
  (`ScheduledJobRun` is `[NoAudit]`).
- **Configurable (2026-07-20, reminders follow-up 01).** The `RetentionYears` / `KeepLastRunsPerJob`
  **constants were replaced by `SchedulerRetentionOptions`** — a subclass of the shared
  `framework/Sydowwe.Framework/…/retention/RetentionOptions.cs` (section `SchedulerRetention`), so a deployment can lengthen the window or freeze the ledger (`Enabled = false`) without a rebuild. Defaults are
  unchanged at 3 years / keep last 20 per job. Two tests added (kill switch, non-default floor).
  <br>**The query itself was deliberately left as plain LINQ.** That follow-up first routed it through a shared generic `DeleteExpiredKeepingLastNAsync` helper; that was reverted the same day. The
  helper absorbed only the trivial half (age + keep-last-N) into expression-tree machinery that left the type system, while the `ReplaysRunId` lineage guard — the part that actually differs per
  ledger — stayed in the caller regardless. Four short queries did not justify it. Share the *policy*, not the query.

### L2 — `PayloadSnapshotJson` / `PayloadJson` carry arbitrary opaque data with PII hygiene by convention only — **LOW (conditional) — ✅ FIXED 2026-07-06**

- **Rule.** GDPR Art. 5 (1)(c) data minimisation + Art. 32 (security of processing); zák. 18/2018 §39.
- **What the code does today.** The payload is "an opaque `Payload`" stored verbatim as `jsonb`
  (`ScheduledJob.PayloadJson` l.33; `ScheduledJobRun.PayloadSnapshotJson` l.50). The only guard is a doc convention ("handlers own PII hygiene") and a comment. The by-id run-detail endpoint
  **deliberately surfaces `PayloadSnapshotJson`** ("the one place that does", summary l.186), and
  `ScheduledJob.PayloadJson` is **audited** — it has no `[AuditIgnore]` (`ScheduledJob.cs:33-34`), so a payload edit writes before/after JSONB snapshots into `audit_log`, which is *itself* retained
  for years and PII-redacted only for recognizable shapes (emails/IBAN/birth-number — free-text names leak, per root `CLAUDE.md` logging rule).
- **Consequence.** Zero risk *today* (payloads are static schedule config: cron strings, module keys). Becomes a real leak the moment an owner passes a per-subject payload (an employee id is fine; a
  name, address, or free-text reason is not) — it then lands in three durable stores (run log, audit_log, and the admin detail view) none of which erasure reaches.
- **Fix direction.** (a) Add `[AuditIgnore]` to `ScheduledJob.PayloadJson` — the schedule's *identity*
  (key/cron/status) is the audit-worthy part; the opaque body is not, and the run log already snapshots it. (b) Promote the "no PII in payloads — pass ids, not free-text" rule from a summary aside to
  a hard line in the `IScheduledJobHandler` / `RecurringJobRegistration` contract XML-doc, where owners actually read it. No schema change.
- **Done (2026-07-06).** `[AuditIgnore]` added to `ScheduledJob.PayloadJson` (`ScheduledJob.cs:34`). No-PII rule promoted into XML-doc on `RecurringJobRegistration.Payload`,
  `ScheduledJobContext.GetPayload`. No migration needed (interceptor-driven). Build confirmed green.
- **Follow-up (2026-07-21, `prompts/payload-pii-contract.md`).** The rule the 07-06 fix promoted into three separate XML-docs is now **stated once**, in
  `Sydowwe.Framework.Contracts/notification/payload/INotificationPayload.cs`, and
  `ScheduledJob.PayloadJson` / `ScheduledJobRun.PayloadSnapshotJson` point at it rather than restating a local convention. That is the whole of what changed here, deliberately.
- ⚠️ **Residual: Scheduler job payloads are NOT type-constrained, and that is a decision, not an omission.**
  The cross-module refactor introduced `INotificationPayload` / `IReminderPayload` markers plus a reflection guard test, and closed the `object?` hole across the entire notification path.
  `ScheduledJob.PayloadJson`
  was left `object`-shaped because it is a **different risk class**: handler *configuration* ("purge older than X"), not per-subject content — which is exactly why this finding is MEDIUM/LOW and
  Notifications' was HIGH. Introducing an `IJobPayload` marker would touch every registrar for no live exposure. If a handler ever takes a per-subject payload, that is the trigger to add the marker
  and fold job payload types into
  `PayloadPiiContractGuardTests` — the test already walks markers generically, so it would be a one-line addition there plus the marker itself.

### L3 — no findings on statutory figures (documented negative)

Phase A confirmed the module hard-codes **no** effective-dated rate, holiday catalog, quota, threshold, or deadline. `OverduePolicy.GraceMargin = 60s` and `RecentWindowHours = 24` are operational
tuning constants, not legal figures — correctly *absent* from `routineLawCheckups.md`. This is the honest
"nothing to pin" result, recorded so a future audit doesn't re-hunt it.

---

## Part 2 — Segment fit

A < 100-employee Slovak company runs this substrate headless: it wants recurring jobs to *fire reliably* and to *find out when they don't*. Ranked against that:

1. **Failure alerting / dead-letter (top gap).** A recurring job that starts failing — a payroll or §36 export, `AnonymizeTerminatedEmployeesJob`, a reminder scan — leaves `LastOutcome=Failed` and a
   `Failed` run row, and **nobody is notified**. `GetSchedulerHealthEndpoint` is pull-only; a company with no dedicated ops person never opens it. The substrate should raise a notification on a
   failure (or on N consecutive failures / an overdue job) — *without* Scheduler depending on Notifications (forbidden). The clean seam already exists: emit through a `Sydowwe.Framework.Contracts` notification contract or
   let an owner-side handler subscribe. **This is the single most valuable addition for the segment.**
2. **Automatic retry on transient failure.** Today a failed fire simply waits for the next scheduled fire; `MisfirePolicy` covers *missed* fires (process down), not *failed* ones. A transient DB blip
   fails a monthly job for a whole month. A small configurable retry-with-backoff (e.g. 3× over 15 min)
   on `Failed` would remove the most common "why didn't it run" support ticket.
3. **Local-time (Europe/Bratislava) scheduling option.** See L-adjacent code note C1 — for calendar- boundary and "run at 03:00" jobs, wall-clock-local is what owners expect. UTC-only is a footgun.
4. **"Run once / at a specific datetime" (`ScheduleSpec` one-shot).** `ScheduleSpec` explicitly has
   "No one-shot" (domain-map l.81). Small companies routinely want "run this migration/backfill once tonight." Currently only cron/interval; a one-shot would round out the primitive cheaply.

**Deliberate non-goals (scope discipline).** *Do not* build: Quartz clustering / persistent store / DB-backed distributed locks (single-node is the deployment axiom — `[[project-deployment-model]]`);
a visual cron-builder or drag-drop schedule UI (Admin-only diagnostic surface, not an end-user feature); job-body logic of any kind in this module (the whole point of the split); per-tenant scheduling
(single deployment = single company). These stay out permanently, not "later".

---

## Part 3 — Code-level notes (load-bearing only)

### C1 — DST drift for wall-clock-anchored jobs — **FIXED 2026-07-06 (D3/B4)**

*Original finding.* `SchedulerService.cs:215` (cron) and `:233-237` (Day/Week/Month/Quarter/Year calendar intervals) all pinned `InTimeZone(TimeZoneInfo.Utc)`. Consequences for owners who think in
Slovak local time:

- A cron `0 0 3 1 * ?` ("03:00 on the 1st") fired at **03:00 UTC = 04:00 CET / 05:00 CEST** — and the hour *shifted* across the March/October DST changes. A job an operator set for "3am" ran at "5am"
  half the year.
- More sharply, a **calendar-boundary** job (attendance year rollover, `ProvisionNextYearAttendance`,
  `RolloverLeaveBalances` — phase-05 candidates) anchored near midnight local could fire on the *wrong local calendar day/year* because 00:00 CET is 23:00 UTC the previous day.

**What shipped.** `ScheduleSpec` now carries an optional IANA `TimeZoneId` (`FromCron`/`Every` overloads), resolved via `TimeZoneInfo.FindSystemTimeZoneById` and threaded into **every**
`.InTimeZone(...)` call in
`SchedulerService` (cron + all five calendar-interval presets). **Default = UTC when `TimeZoneId == null`**
— chosen over "Bratislava-for-calendar-intervals" so *no already-registered job's fire instant moves silently*; the wall-clock anchor is an explicit per-registration opt-in (`"Europe/Bratislava"`),
which is also what the phase-05 migrations must set to preserve legacy server-local fire times. An unknown id is rejected at registration (`ArgumentException`), not at fire time. Two extra correctness
details surfaced implementing the fix:

- **Calendar intervals also needed `PreserveHourOfDayAcrossDaylightSavings(true)`.** `InTimeZone` alone is insufficient — a Quartz `CalendarIntervalTrigger` advances by a fixed elapsed span, so its
  hour-of-day still drifted by the DST offset across a transition until the hour was explicitly preserved. This is exactly the calendar-boundary case the finding was most worried about; the test
  `DailyCalendarIntervalInBratislava…`
  caught it before it shipped.
- `TimeZoneId` is persisted on `ScheduledJob` (+ EF config + `ScheduledJobDto` + migration
  `AddScheduledJobTimeZoneId`) so the dashboard reflects the anchor zone.

**Tests.** `MojaDigitalnaFirma.AdminPortal.Tests/unit/scheduler/SchedulerTimeZoneTests.cs` pins the fix with pure Quartz fire-time math across **both** fixed 2026 Slovak DST transitions
(spring-forward 2026-03-29, fall-back 2026-10-25): a Bratislava cron and a Bratislava daily calendar interval both hold 03:00 **local**
on both sides, while a UTC job holds 03:00 UTC and *drifts* to 04:00/05:00 local (the documented pre-fix behaviour, asserted so the UTC default can't regress). **Linux caveat:** IANA-id resolution
needs OS `tzdata`
in the container image (standard on the aspnet base images).

### C2 — dashboard/exports non-authoritative until phase 05 completes

Already documented (summary l.170-174) and accepted — repeated here only so the audit trail is honest:
the overview/health/run-history views reflect **only** registry-registered jobs. With 7 of 8 legacy jobs still hand-wired in the host `AddQuartz` block, "N scheduled jobs" is an undercount and the
health view's failed/overdue signals are blind to the majority of live recurring work. This is a *known, accepted* gap (delta-audit rule) — the fix is finishing phase 05, tracked in B1.

### Accepted gaps (agreement)

- **RAM job store, no persistence/clustering** — correct for single-node; agreed, non-goal.
- **A prior pause does not survive restart** (RAM store) — acceptable single-node observability, agreed.
- **Crash mid-run leaves no run row** — recovery via next fire + misfire policy; agreed given append-only terminal-row design.
- **Orphaned registrations not auto-deleted** — correct (preserves run-log history); surfaced read-side in the health view. Agreed.

---

## Part 4 — Open questions for the owner

- **Q1 (→ L1, recommended first).** Ship `Scheduler.PurgeExpiredRunLogs` (self-hosted on the substrate)? If yes, what retention window — **24 months**, or tie it to the audit-log retention you already
  run? And keep-last-N-per-job floor so no job's history fully empties?
- **Q2 (→ Part 2 #1, highest product value).** Add **failure alerting**? If yes, the seam decision:
  Scheduler emits via a `Sydowwe.Framework.Contracts` notification contract (Scheduler stays decoupled, one new contract), **or** owners opt in per job (a flag on `RecurringJobRegistration` + an owner-side subscriber).
  Which fits your decoupling posture? And the trigger: first failure, or N consecutive / overdue-past-margin?
- **Q3 (→ C1, blocks phase 05 quality).** Add an optional **timezone** to `ScheduleSpec`
  (default `Europe/Bratislava` for calendar intervals)? This should land **before** phase 05 migrates the attendance calendar-boundary jobs, or those migrations risk shifting *when* a job runs — a bug
  by the migration's own "behaviour-preserving" rule.
- **Q4 (→ L2).** Add `[AuditIgnore]` to `ScheduledJob.PayloadJson` and harden the "no free-text PII in payloads" rule into the `IScheduledJobHandler` contract doc? (Cheap, no migration.)
- **Q5 (→ Part 2 #2/#4).** Priority of **auto-retry** and **one-shot `ScheduleSpec`** relative to the above — nice-to-haves, or wanted in the next build slice?
- **Q6 (→ B1).** Confirm the **phase-05 migration order** and that it stays one-owner-per-commit; the dashboard is not authoritative until it's done, which caps the value of everything in Part 5.

**Recommended priority:** Q3 (timezone) → then finish phase 05 (Q6/B1) → Q1 (retention) → Q2 (alerting)
→ Q4 → Q5. Rationale: the timezone seam is cheap and must precede the calendar-boundary migrations; completing 05 makes the dashboard trustworthy, which is the precondition for alerting to be worth
wiring; retention and alerting then compound on an authoritative registry.

---

## Part 5 — Bigger product ideas

### B1 — Finish phase 05: migrate the remaining 7 legacy jobs onto the substrate — **fit: high, risk: low, effort: 3-4 phases**

The substrate only pays off once it's authoritative. 7 of 8 tracked jobs (2× `Core` logs/audit, 3× `EmployeeModule`, 3× `Attendance`) still run hand-wired outside the registry (summary tracker
l.294-304). Migrate one owner per commit per the existing recipe. **Risk:** the `StartNow` startup- catch-up triggers (`RetryPendingStorageDeletions`, `MarkLeaveDone`) and calendar-boundary jobs must
preserve *when* they fire — do C1/Q3 first. **Effort:** ~1 phase per owning module (Core, Employee, Attendance) + a shared timezone seam phase.

### B2 — Failure-alerting seam (Scheduler → Notifications via Contracts) — **✅ DONE 2026-07-19**

The Part-2 #1 gap as a build (owner-approved D2), landed as follow-up 05 after B5's retry work. **Shipped:**

- **`Sydowwe.Framework.Contracts` seam** `IJobFailureNotifier.NotifyJobFailedAsync(JobFailureAlert)` (+ the PII-free
  `JobFailureAlert` record — `JobKey` / `OwnerModule` / `ErrorType` / `RunId` / `FailedAtUtc`, never the raw `ErrorMessage`). Scheduler is the producer; **it still references no domain module** — the
  arrow is Scheduler → `Sydowwe.Framework.Contracts` ← Notifications, exactly like `IScheduler`.
- **Per-job opt-out** `AlertOnFailure` (default **true**) on `RecurringJobRegistration` → `ScheduledJob`
  (+ EF config `HasDefaultValue(true).ValueGeneratedNever()` so an opt-out `false` actually persists — the same inverted trap as `MaxRetries`) → `ScheduledJobDto` + `RegisterJobRequest`. No
  `Importance` enum — a single bool, as decided.
- **Emission points in the dispatcher — all best-effort + failure-isolated + AFTER the run row commits.**
  The invariant is *"this failure is terminal"*, not merely *"retries are exhausted"*:
  (1) the exhausted-retries branch in `InvokeAndCaptureAsync` — fires *only* when the `Failed` run was the **final** attempt (`retryAttempt >= MaxRetries`, incl. `MaxRetries == 0`), never on a
  non-final failure that still has a retry queued; (2) the `HandlerNotFound` misconfiguration path in `Execute`, which never enters the retry loop but is precisely what an unattended owner must hear;
  (3) **a retry that could not be armed** (`ScheduleRetryAsync` returns `false` when the retry scheduler throws) — no further attempt is coming, so that failure is terminal too. Without (3) a lost
  retry would go silent until the next scheduled fire: up to a month for the monthly purges, a year for the calendar-boundary jobs, and **never** for a one-shot. A manual/replay failure does **not**
  alert (an operator is watching). A throwing notifier is caught + logged and never fails the run.
- **Alert-storm control** `IJobAlertThrottle` / `JobAlertThrottle` (in-process singleton, mirroring
  `IJobConcurrencyGate`): **one alert per `JobKey` per hour**. Added because the shipped combination of
  "alert on every terminal failure" + email-default-on would page every Admin hundreds of times a day for a frequently-scheduled job stuck failing (`reminders.scan` defaults to **every 5 min**).
  **Only the notification is throttled — every failure still reaches `scheduled_job_run` and the log.** Per-key, so a noisy job can't mask another one breaking; resets on restart (first failure after
  restart always alerts). *(Note: the tempting cheap alternative — "suppress if the previous run was already Failed" — is wrong here:
  retry chains set `LastOutcome = Failed` mid-chain, so it would suppress the primary final-attempt alert.)*
- **Live consumer** `JobFailureNotifier` (`Core.Notifications`, `IScopedService`) delivers a
  `NotificationType.ScheduledJobFailed` in-app/push/ **email** (default-on — operational, not marketing) to Admin + RootAdmin via `INotificationService`. Registered as the auto-scanned real impl;
  `NoOpJobFailureNotifier` (Core.Scheduler) is the `TryAddScoped` fallback for a Quartz-host without Notifications. **Not an unplugged seam** — `JobFailureNotifierLiveConsumerTests` proves the
  HBCleaning host resolves the real notifier and an alert reaches the notification pipeline.
- **Tests:** `ScheduledJobFailureAlertDispatcherTests` (final-failure alerts once; opt-out / success emit none; non-final failure stays silent; misconfiguration alerts; manual fire doesn't; throwing
  notifier is isolated) + the live-consumer test. HBCleaning migration `AddScheduledJobAlertOnFailure` (+ Sandbox).

**~~Residual gap (accepted, not built)~~ — ✅ CLOSED 2026-07-21 by follow-up 08.** Alerting used to cover only jobs that *run and fail*; a job that never fires at all — scheduler dead, registration
missing, RAM-store trigger lost on restart — produced no `Failed` run and therefore no alert, leaving overdue detection pull-only in the health view (04a). Now built: `OverdueJobSweepJobHandler`, a
Scheduler-owned recurring job (every 10 min, configurable) that reuses `OverduePolicy.WhereOverdue` and pushes through the **same** `IJobFailureNotifier` seam, widened rather than duplicated
(`RunId` → `long?`, `FailedAtUtc` → `DetectedAtUtc`, `+ ExpectedRunAtUtc`,
`+ JobAlertKind`). Lands as its own `NotificationType.ScheduledJobOverdue`, throttled 12h in a
`"overdue:{JobKey}"` bucket. **One residual remains and is accepted:** the sweep cannot detect its own absence or a dead process — only something outside the process can. The host now exposes the seam
for that (`/health/live`, no checks, anonymous — plus `/health/ready` for dependencies); pointing a monitor at it and routing its alerts is ops configuration, not application code. See
`docs/summary.md` §"Overdue sweep" for the full reasoning and the five settled design questions.

**Follow-up 06 (2026-07-21) — the three policy values 05 picked without owner sign-off, now reviewed:**
throttle window (D1, `JobAlertThrottle.Window`) kept at 1h — it only matters for 5-min-cadence jobs, and the monthly/yearly population never re-triggers it anyway; email default-on (D2) kept,
explicitly contingent on D1's throttle staying in place; export columns (D3) — `TimeZoneId` / `MaxRetries` / `AlertOnFailure` /
`RunAtUtc` added to `SchedulerExportService.ExportJobsOverview`. See `docs/summary.md` §"Failure alerting" for the full reasoning.

### B3 — Self-retention purge job — **✅ DONE 2026-07-06**

L1 as a build; dogfoods the substrate (a Scheduler-owned handler + registrar, exactly like the audit- log purge it already migrated). The "near-zero risk" call was almost right — the one real hazard
found in implementation was the `ReplaysRunId` Restrict FK (see L1's Done block).

### B4 — Timezone-aware `ScheduleSpec` — **timezone part ✅ DONE 2026-07-06; one-shot deferred to B5**

C1/Q3. **Shipped:** an optional IANA `TimeZoneId` on `ScheduleSpec` threaded into every `.InTimeZone(...)`
call (see C1 above for the full write-up + tests + the `PreserveHourOfDayAcrossDaylightSavings` detail). **Default deviation from the audit's suggestion:** the audit proposed "default
`Europe/Bratislava` for calendar intervals, UTC opt-in for infra jobs" (a *per-kind* default). Implementation chose a **single global default of UTC** (`TimeZoneId == null` ⇒ UTC) for both cron and
calendar intervals instead, because the overriding rule is *do not silently change an already-registered job's fire instant* — a per-kind default would have moved every existing calendar-interval
job's wall-clock by the DST offset on first re-registration. Wall-clock local time is therefore an explicit per-registration opt-in; phase-05 migrations set `"Europe/Bratislava"` where they need to
preserve legacy server-local timing. The **`RunOnceAt(datetime)` one-shot** was **not** built here — it is independent of the timezone bug and belongs with the retry work in **B5/D5**.

### B5 — Auto-retry-with-backoff on `Failed` (+ one-shot `ScheduleSpec`) — **✅ DONE 2026-07-19**

**Shipped:** `RecurringJobRegistration.MaxRetries` (default 3, `0` disables) → `ScheduledJob.MaxRetries`;
`ScheduledJobRun.RetryAttempt`; `TriggerSource.Retry`; `IJobRetryScheduler`/`JobRetryScheduler` arming a Quartz one-shot at `now + 1 min × 2^(attempt-1)`; and `ScheduleSpec.RunOnceAt` →
`JobScheduleType.Once` +
`ScheduledJob.RunAtUtc`. Migrations: `AddScheduledJobRetryAndOneShot` (HBCleaning). Tests green (`ScheduledJobRetryDispatcherTests` 6, `ScheduledJobOneShotDispatcherTests`; full scheduler suite 171
passed).

**The two risks called out below both landed as predicted, and both are now pinned by tests:**

- *Dedup interaction* — retries carry a **null `ScheduledFireTime`**, keeping them outside the terminal-row partial-unique filter so a retry is never dropped as a `DuplicateFire`; `DisallowConcurrent`
  still vetoes an overlapping retry.
- *One-shot boot re-fire (found on recheck, not in the original audit)* — because owners re-register on every boot, an already-run one-shot would have re-fired immediately via the past-due misfire
  path. Solved by carrying the fixed instant on the trigger and using it **as** the fire's `ScheduledFireTime`, so the re-fire dedups to the same occurrence against the run log (the only "already ran"
  memory that survives a restart).

**Documented, not fixed:** a pending backoff retry lives in the RAM job store and does **not** survive a process restart — the failed occurrence waits for the next scheduled fire. Consistent with the
module's other RAM-store gaps; stated in `summary.md` so it isn't filed as a bug later.

**Left for B2/follow-up 05:** the exhausted-retries branch in `InvokeAndCaptureAsync` is the alerting seam, exposing "this `Failed` run was the FINAL attempt" (attempt + max in scope).

<details><summary>Original recommendation</summary>

**fit: high (owner-approved D5), risk: medium, effort: 1-2 phases**

Part-2 #2/#4, approved (D5). A retry policy on `RecurringJobRegistration`: **max-attempts settable per-job, default 3**, and **incremental (exponential) backoff** — each retry waits longer than the
last (`base × 2^attempt`, e.g. ~1 → 2 → 4 min) so a struggling dependency has time to recover. The dispatcher re-fires off-schedule (reusing the `TriggerNow`/replay path) on a `Failed` outcome up to
the cap, each attempt a linked run row. **Failure alerting (B2) fires only after the final retry fails.**
Also add a **one-shot** `RunOnceAt(datetime)` to `ScheduleSpec` (small, independent change; `ScheduleSpec`
today explicitly has "no one-shot"). **Risk:** retry interaction with `DisallowConcurrent` and the dedup index (a retry of the same occurrence must not be dropped as a `DuplicateFire` — retries carry
a null
`ScheduledFireTime` like other off-schedule fires, which keeps them outside the dedup filter); effect-idempotency remains the handler's responsibility (same caveat as replay).

</details>

### Recommended order + effort

| Order    | Item                                                                     | Phases | Main risk                                                                                                                                                                                   |
|----------|--------------------------------------------------------------------------|--------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| ~~1~~ ✅ | B4 — timezone `ScheduleSpec` (**done 2026-07-06**; one-shot moved to B5) | 1      | DST/boundary correctness — pinned by `SchedulerTimeZoneTests`                                                                                                                               |
| 2        | B1 — finish phase-05 migration (7 jobs)                                  | 3-4    | preserve wall-clock fire time (set `"Europe/Bratislava"` explicitly); `StartNow` catch-up                                                                                                   |
| ~~3~~ ✅ | B3 — self-retention purge job (**done 2026-07-06**)                      | 1      | `ReplaysRunId` Restrict FK — handled via lineage exclusion                                                                                                                                  |
| ~~4~~ ✅ | B5 — auto-retry-with-backoff (+ one-shot) (**done 2026-07-19**)          | 1-2    | dedup-index / `DisallowConcurrent` interaction — handled via null `ScheduledFireTime`; one-shot boot re-fire handled via occurrence dedup                                                   |
| ~~5~~ ✅ | B2 — failure-alerting seam (**done 2026-07-19**)                         | 1-2    | stayed decoupled via the `Sydowwe.Framework.Contracts` `IJobFailureNotifier` seam; fires only on a *terminal* failure (final retry / unarmable retry / `HandlerNotFound`), throttled per key to avoid alert storms |
