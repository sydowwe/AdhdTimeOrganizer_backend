# Scheduler — Agent Summary

**Purpose:** The generic time substrate — owns the scheduling *infrastructure* and *invocation* (Quartz config, a recurring-job registry, an append-only run log, a keyed dispatcher), **never the job
bodies**.

**Bounded context:** Owns *when* recurring work fires and *how to observe it*. Does **NOT** own any job logic, any domain data, or notification delivery. The work stays in the owning module, exposed
as a keyed `IScheduledJobHandler` the dispatcher invokes.

## The three-module split (do not blur it)

- **Notifications** (`Core.Notifications`) = how/where to send.
- **Scheduler** (this) = the generic time substrate.
- **Reminders** (`Core.Reminders`, parallel) = user-facing deadline notifications; a *consumer* that registers exactly one recurring scan job here and dispatches through Notifications.

> **Crisp rule:** Scheduler owns scheduling infrastructure + invocation, never job bodies. If you ever
> want `using AdhdTimeOrganizer.<Domain>` in this module, the design is wrong — stop and flag it.

## Dependency seams

- **Exposes:** the `Sydowwe.Framework.Contracts.scheduling` contract (`IScheduler`, `IScheduledJobHandler`,
  `RecurringJobRegistration`, `ScheduleSpec`, `ScheduledJobContext`, `ICronEvaluator`, `IJobFailureNotifier` +
  `JobFailureAlert` + the `JobScheduleType` / `JobIntervalPreset` / `MisfirePolicy` / `TriggerSource` enums). Owners depend on **`Sydowwe.Framework.Contracts`**, never on this module — the arrow points owning module →
  `Sydowwe.Framework.Contracts` ← Scheduler.
- **`IJobFailureNotifier`** (`Sydowwe.Framework.Contracts` seam, follow-up 05) — the Scheduler **announces** a terminal job failure through this without ever referencing a delivery module; a consumer implements it. Live
  impl:
  `Core.Notifications`'s `JobFailureNotifier` (delivers to Admin + RootAdmin); `NoOpJobFailureNotifier` (this module, `TryAddScoped` fallback in `AddCore`) covers a Quartz-host that ships no
  Notifications. Same shape as the retry seam: the arrow points Scheduler → `Sydowwe.Framework.Contracts` ← Notifications.
- **`ICronEvaluator`** (`infrastructure/CronEvaluator.cs`, `ISingletonService`) — a stateless cron validate + next-fire helper over Quartz's `CronExpression`, so the Quartz dependency stays owned here
  and consumers (e.g. the Reminders module's per-occurrence cron math) reason about cron through the contract alone. Pure expression math — no `ISchedulerFactory` — so unlike `SchedulerService` it's
  safe under the blanket DI scan even in hosts that never call `AddQuartz`.
- **Consumes:** nothing domain-specific. Only `Sydowwe.Framework.Contracts` + `Sydowwe.Framework`.
- The Core-only enums `JobStatus` and `RunOutcome` live in this module (they describe stored state, not the contract).

## Gotchas — things that will bite you

- **`JobKey` is the idempotency key** — a unique DB index, not a code convention.
  `RegisterRecurringJobAsync` is an upsert by `JobKey`; re-registering on every boot is safe.
- **`scheduled_job_run` is append-only.** One INSERT per execution, written at completion with
  `StartedAt` + `FinishedAt` + a terminal `Outcome` together. `RunOutcome` has **no** "Running" value. Never update or delete a run row — corrections are new reversal/replay rows linked via
  `ReplaysRunId`. **Consequence:** a process crash mid-run leaves *no* run row; recovery is the next scheduled fire + the misfire policy, not a stuck row. In-flight visibility comes from
  `ScheduledJob.Status` + the in-process concurrency gate.
- **Not user-scoped.** Both entities derive from `BaseTableEntity` (not `BaseEntityWithUser`) so a background registrar/dispatcher with no authenticated user can write them without the `UserId == 0`
  FK trap. Don't "fix" this by adding a user FK.
- **No PII in `ScheduledJobRun.ErrorMessage`** — handlers own that. Log a non-PII id/correlation id.
- **A job body MAY dispatch FastEndpoints commands — the dispatcher provides the ambient scope.**
  The FE in-process command bus resolves a handler's *scoped* services from
  `IHttpContextAccessor.HttpContext.RequestServices` and falls back to the **root** provider without one, where a scoped `DbContext` won't resolve. A Quartz fire has no ambient `HttpContext`, so
  `ScheduledJobDispatcher.EnsureAmbientDispatchScope()` installs one (`??=`, pointed at the fire's own DI scope) before invoking anything. **Don't re-add a local `HttpContext ??= …` workaround in a
  handler** — the guarantee is here, pinned by `ScheduledJobDispatchScopeTests`. Before this (follow-up 07) every such dispatch threw, and any caller with a best-effort `try/catch` degraded **silently
  and permanently**. ⚠️ **Legacy raw Quartz `IJob`s do not get this** — they bypass the dispatcher entirely (see phase 05).
- **Don't add `UsePersistentStore` / Quartz clustering.** Single-node modular monolith by design; correctness against double-fires lives in our Postgres run log + dedup, not in Quartz.

## Quartz config (centralised here, applied by the host — phase 02a)

- **One `AddQuartz`, owned by the host; defaults + dispatcher owned by Scheduler.** The host's single
  `services.AddQuartz(q => …)` calls `q.AddSchedulerQuartzDefaults()` (in
  `infrastructure/SchedulerQuartzConfig.cs`) first, then keeps its existing per-module
  `q.AddJob<DomainJob>()` + trigger lines — those reference domain modules Scheduler must not `using`, so they stay in the host until phase 05 migrates them to `IScheduler` registration.
- **The single generic dispatcher job** `ScheduledJobDispatcher` (`application/job/`) is registered **`StoreDurably()`** so it exists with no trigger of its own; every recurring trigger (created per
  registration in 02b) points at it. Its `Execute` body lands in **phase 03** (below) — never
  `[DisallowConcurrentExecution]` (that would serialise *all* jobs; per-key concurrency is an in-process gate in 03).
- **Hosted-service options** (`WaitForJobsToComplete = true`) are centralised as
  `SchedulerQuartzConfig.ConfigureSchedulerHostedService`, passed to the host's `AddQuartzHostedService`.
- **Persistent-store / clustering is a deliberate decision point, left OFF** — single-node modular monolith uses the RAM job store; revisit only on a (not-anticipated) move to multi-node. See the
  commented decision point in `AddSchedulerQuartzDefaults`.

## Registration & control — the `IScheduler` impl (phase 02b)

`infrastructure/SchedulerService.cs` implements the `Sydowwe.Framework.Contracts.scheduling.IScheduler` contract, auto-registered via the `IScopedService` marker and **safe with no authenticated user** (registry entities
aren't user-scoped). It injects the `DbContext`, Quartz's `ISchedulerFactory`, and the set of registered
`IScheduledJobHandler`s.

- **`RegisterRecurringJobAsync` is an idempotent upsert by `JobKey`:** upserts the registry row, then builds one trigger (identity = `JobKey`, group `scheduler`) pointing at the single durable
  dispatcher and carrying `JobKey` in the `JobDataMap`; reschedules in place if the trigger exists (never a duplicate).
  `NextRunAt` is persisted from the trigger's computed next fire time. **Validation fails fast:** invalid cron (`CronExpression.IsValidExpression`), non-positive interval / missing preset, or a
  `HandlerKey` that doesn't resolve to a registered handler all throw `ArgumentException` at registration, not at fire time.
- **Schedule translation matches the unit:** cron → `WithCronSchedule(..., InTimeZone(tz))`;
  `Minute`/`Hour` → `SimpleScheduleBuilder` (fixed-length); `Day`/`Week`/`Month`/`Quarter`/`Year` →
  `CalendarIntervalScheduleBuilder` (variable-length — **never** a fixed-ms approximation), with
  `Quarter` = 3 × months. `MisfirePolicy` maps to the matching Quartz misfire instruction per builder type (Simple has no FireAndProceed/DoNothing names → FireNow / NextWithRemainingCount).
- **One-shot (`ScheduleSpec.RunOnceAt`, phase 04-followup):** `JobScheduleType.Once` → a single non-recurring trigger (`StartAt(runAt)` + `WithRepeatCount(0)`), mirrored onto `ScheduledJob.RunAtUtc`.
  It is an **absolute instant**, so `TimeZoneId` does not apply. A **past-due** instant fires once immediately via the misfire path (`FireNow`) rather than being silently dropped.
  > **⚠️ The boot re-registration trap — and why the one-shot is at-most-once.** Owners re-register every job
  > on **every** boot (required: the RAM store drops all triggers on restart), so a one-shot that already ran
  > would naively be re-scheduled and fire **again** — immediately, via the past-due misfire path. The fix:
  > the trigger carries its fixed instant in the data map (`RunOnceAtDataKey`) and the dispatcher uses **that**
  > as the fire's `ScheduledFireTime` instead of Quartz's computed one. The on-time fire and every later
  > re-registration re-fire therefore dedup to the **same occurrence**, and the run-log dedup (app check +
  > partial unique index) — which *does* survive restarts — records the re-fire as a `DuplicateFire` `Skipped`.
  > The run log is the durable "already ran" memory the RAM store can't provide. Pinned by
  > `ScheduledJobOneShotDispatcherTests`.
- **Time zone (audit C1/B4 fix):** every wall-clock schedule is anchored to the resolved
  `ScheduleSpec.TimeZoneId` (an IANA id, e.g. `"Europe/Bratislava"`), **defaulting to UTC when
  `TimeZoneId == null`** so a pre-existing registration's fire instant never shifts unless its owner opts in. The id is resolved via `TimeZoneInfo.FindSystemTimeZoneById` (validated **at
  registration** — an unknown id throws `ArgumentException`, not at fire time) and threaded into the cron + all five calendar-interval
  `.InTimeZone(...)` calls. Sub-day `Minute`/`Hour` intervals are fixed-length and ignore it. **Calendar intervals also set `PreserveHourOfDayAcrossDaylightSavings(true)`** — `InTimeZone` alone is not
  enough, because a Quartz `CalendarIntervalTrigger` otherwise advances by a fixed elapsed span and its hour-of-day drifts by the DST offset across a transition; preserving the hour re-anchors each
  fire to the original local wall-clock (what a "run at 03:00 local" / calendar-boundary job expects). `TimeZoneId` is mirrored onto
  `ScheduledJob` (+ EF config + `ScheduledJobDto` + migration `AddScheduledJobTimeZoneId`) for the dashboard. DST correctness is pinned by `SchedulerTimeZoneTests` (fire-time math across both 2026
  Slovak transitions).
  > **Linux note:** IANA-id resolution needs the OS `tzdata` package present in the container image (the
  > common `mcr.microsoft.com/dotnet/aspnet` images ship it); Windows/ICU resolves IANA ids natively.
- **`RemoveJobAsync`** unschedules + sets `Status = Removed`, `NextRunAt = null` (keeps the row + run-log history — never deletes). **`PauseJobAsync` / `ResumeJobAsync`** pause/resume the trigger and
  toggle
  `Status`; resume recomputes `NextRunAt`. **`TriggerNowAsync`** fires the dispatcher once off-schedule via
  `TriggerJob` + a `TriggerSource = Manual` data-map flag (phase 03 reads it) — **works even when paused**. Remove/pause/resume on an unknown key are **idempotent no-op successes**.

### Startup-reconciliation contract (owners, not Scheduler)

Scheduler imports no module and **does not enumerate owners** — it only supplies `IScheduler` via DI. Each owning module registers its **own** tiny `IHostedService` (in that module's DI extension,
which the host already wires) that calls `RegisterRecurringJobAsync` for the jobs it owns. "Reconcile on boot" = re-running every owner's registration → the idempotent upsert converges registry +
triggers with no duplicates. This re-registration is **required, not just safe**: the RAM job store loses all triggers on restart, so a job only fires again after its owner re-registers it. Owners
never call `AddQuartz`.

- **A prior pause does not survive a restart** (RAM store drops the trigger; reconciliation recreates it as
  `Active`). Pause/`Status` are runtime observability + control, not durable state — acceptable single-node.
- **Orphaned registrations:** a registry row whose handler/owner is gone is simply **not re-registered** on boot, so it stays `Active`/`Removed` with no live trigger. Scheduler never silently deletes
  it (run-log history is preserved); surfacing it as orphaned is a read-side concern in 04a. `RegisterRecurringJobAsync`
  can't create an orphan — it rejects an unknown `HandlerKey`.

## The dispatcher — execution & run log (phase 03)

`application/job/ScheduledJobDispatcher.cs` is the engine: the single generic Quartz job every trigger points at. It is **generic by construction** — it knows only the `ScheduledJob` registry row, the
run log, and the `IScheduledJobHandler` contract, never a domain type. Resolved per-fire via Quartz's Microsoft-DI integration (so it gets a scoped `DbContext` + the registered `IScheduledJobHandler`
s). Each `Execute`:

0. **Ambient dispatch scope:** point `IHttpContextAccessor.HttpContext` at this fire's DI scope (`??=`, so a real request context is never clobbered) so the job body — and everything it calls — can
   dispatch FastEndpoints commands. See the gotcha above; follow-up 07.
1. **Identity:** read the business `JobKey` from the merged `JobDataMap`; load the registry row. No row → log + return (no FK parent to hang a run row on — only happens off a RAM store anomaly).
   `Status == Removed`
   → write a `Skipped` run (`ErrorType = OrphanedTrigger`) and return (a manual fire after `RemoveJobAsync`).
2. **Resolve handler** by `HandlerKey` over the injected `IEnumerable<IScheduledJobHandler>` (same keyed lookup as 02b's registration validation). Missing → `Failed` run
   (`ErrorType = HandlerNotFound`), **no throw**.
3. **Concurrency gate** (`IJobConcurrencyGate`, singleton, `infrastructure/JobConcurrencyGate.cs`): if the registration set `DisallowConcurrent`, a **non-blocking** per-`JobKey` try-acquire. Already
   held → `Vetoed`
   run (`ErrorType = ConcurrencyVeto`), skip the overlapping fire (never queue). Released in a `finally`. This in-process gate is the **authoritative** `DisallowConcurrent` enforcement on single-node
   (one process).
4. **Build context + overrides:** `ScheduledJobContext` with the scheduled/actual fire times, payload,
   `CorrelationId` (`Activity.Current?.TraceId`), and `TriggerSource`. Off-schedule overrides come from the
   `JobDataMap` so trigger-now (02b) and replay (04b) reuse this **one** execution path: `TriggerSource`
   (`Manual`/`Replay`, default `Scheduled`), a **payload override** (`PayloadOverrideDataKey` → used as both the context payload *and* the run's `PayloadSnapshotJson`, so a replay re-runs the
   snapshotted payload), and **`ReplaysRunIdDataKey`** (replay lineage). A `Manual`/`Replay` fire has `ScheduledFireTime = null`.
5. **Dedup safety net:** for a `Scheduled` fire only, if a run with the same
   `(ScheduledJobId, ScheduledFireTime)` already executed (`Succeeded`/`Failed`), write a `Skipped` run (`ErrorType = DuplicateFire`) and return — a misfire catch-up / double-fire of the same instant
   produces **no second effect**. The app-level check covers the common sequential case; its **durable** backstop is a **partial unique index** on `(ScheduledJobId, ScheduledFireTime)` over terminal
   (`Succeeded`/`Failed`) rows (`scheduled_fire_time IS NOT NULL`). The per-key gate is the authoritative dedup only for
   `DisallowConcurrent` jobs; for a non-concurrent job two fires of the same occurrence could both pass the
   `AnyAsync` check, so the unique index is what makes the ledger one-execution-per-occurrence race-free (the insert in step 7 catches the `UniqueViolation` and records a `DuplicateFire` `Skipped`
   instead).
6. **Invoke with failure isolation:** `StartedAt` is held **in memory** (no in-progress row — every run row is terminal). `await handler.ExecuteAsync(ctx, ct)` in try/catch: success → `Succeeded`;
   throw → `Failed`
    + `ErrorType`/`ErrorMessage` (handlers own PII hygiene). A handler throwing **never** bubbles out of the job.
7. **Write once at completion:** insert **one** `ScheduledJobRun` (start/finish/duration/outcome/error/ snapshots/`ReplaysRunId`) in its **own** `SaveChanges` first — the run row is append-only (no
   concurrency token), so committing it independently guarantees a successfully-run handler always leaves an audited ledger row. If this insert hits the dedup partial unique index (a racing concurrent
   execution of the same occurrence already committed its terminal row), the `UniqueViolation` is caught, the would-be duplicate row is detached, and a `DuplicateFire` `Skipped` is recorded instead.
   Then, in a **second, best-effort** save, update the registry's `LastRunAt`/`LastOutcome` and — **only for a `Scheduled` fire** — recompute `NextRunAt` from the trigger; because the registry row
   carries a `row_version` token, a concurrent control op (pause/resume/remove/re-register) can bump it between our tracked load and this save, so a `DbUpdateConcurrencyException` here is
   **swallowed** (these fields are disposable observability the ledger already captured, refreshed on the next fire). The non-invoking outcomes (orphan / missing handler / veto / dedup) write their
   run row but **do not** touch
   `LastRunAt`/`LastOutcome`/`NextRunAt` (the in-flight or already-completed run owns those). Run rows are **inserted, never updated**.

### Auto-retry with backoff (phase 04-followup)

A `Failed` run is retried automatically, off-schedule, with **incremental (exponential) backoff**.

- **Per-job cap:** `RecurringJobRegistration.MaxRetries` → `ScheduledJob.MaxRetries`, **default 3**; `0`
  disables retries. Backoff is `1 min × 2^(attempt-1)` (≈ 1 → 2 → 4 min), with the exponent capped so a large cap can't overflow. Each attempt is its own linked run row carrying
  `ScheduledJobRun.RetryAttempt`
  (`0` = the original fire).
- **Only unattended runs retry.** Gated on `TriggerSource.Scheduled` (or a prior `Retry` continuing the chain). A failed `Manual` trigger-now or `Replay` never retries — an operator is watching and
  can re-fire.
- **One execution path.** The retry re-fires the **same durable dispatcher** through a Quartz one-shot trigger at `now + backoff` (`IJobRetryScheduler` / `JobRetryScheduler`), exactly like trigger-now
  and replay — it never forks a second path, and never blocks the worker thread with a delay. The interface exists so tests substitute a recording fake and drive retries deterministically instead of
  waiting minutes.
- **⚠️ Dedup interaction (the subtle one):** a retry carries **`ScheduledFireTime = null`**, like every other off-schedule fire. That keeps it outside the `scheduled_fire_time IS NOT NULL`
  partial-unique dedup filter, so a retry of a failed occurrence is **never** silently dropped as a `DuplicateFire`. Do not "fix" a retry to carry the original occurrence's instant — that would make
  the first retry dedup against the run it is retrying and disable the feature entirely.
- **Respects `DisallowConcurrent`** — a retry overlapping a live run of the same key is `Vetoed` by the in-process gate like any fire (it is skipped, not queued).
- **Retry vs Paused/Removed:** a queued backoff trigger is *independent* of the job's recurring trigger, so pausing a job after a failure does **not** cancel its armed retry. The dispatcher re-checks
  live `Status` at retry-fire time and records `Skipped` (`ErrorType = JobNotActive`) when the job is no longer `Active`.
- **Effect-idempotency stays the handler's responsibility** — a retried non-idempotent job repeats its side effects (same caveat as replay).
- **Retries don't survive a restart (documented, not fixed).** The pending backoff trigger lives in the RAM job store, so a process restart drops it; the failed occurrence then simply waits for the
  job's next scheduled fire (a one-shot has none). Accepted single-node behaviour, consistent with the module's other RAM-store gaps (pauses and triggers don't survive restart either) — **not a bug,
  don't file it as one.**

### Failure alerting (phase 05-followup — B2/D2)

A **terminal** job failure raises a push alert through the `Sydowwe.Framework.Contracts.scheduling.IJobFailureNotifier` seam, turning the pull-only health view into push. Scheduler never references a delivery module.

- **On by default, per-job opt-out:** `RecurringJobRegistration.AlertOnFailure` (default `true`) →
  `ScheduledJob.AlertOnFailure` (EF `HasDefaultValue(true).ValueGeneratedNever()` so an opt-out `false`
  persists — the inverted `MaxRetries` trap) → `ScheduledJobDto` + `RegisterJobRequest`. **One bool, no
  `Importance` enum** (decided).
- **The invariant is "this failure is TERMINAL", not merely "retries are exhausted."** Three emission points, all gated on `AlertOnFailure`, best-effort, and AFTER the run row commits:
  (1) the **exhausted-retries** branch in `InvokeAndCaptureAsync` — fires only when the `Failed` run **was**
  the final attempt (`retryAttempt >= MaxRetries`, incl. `MaxRetries == 0`); it must **not** alert on a non-final failure (a retry still queued), a distinction it can't recompute cheaply from run
  rows, so attempt + max are carried on the fire. (2) the **`HandlerNotFound`** misconfiguration path — never enters the retry loop, but a silently-misconfigured job is exactly what an unattended
  owner must hear. (3) **a retry that could not be armed** — `ScheduleRetryAsync` returns `false` when
  `IJobRetryScheduler.ScheduleAsync` throws, which means no further attempt is coming, so that failure is terminal and alerts. ⚠️ **Don't "simplify" this back to alerting only on the exhausted
  branch:** a lost retry would then go silent until the job's next scheduled fire — up to a month for the monthly purges, a year for the calendar-boundary jobs, and **never** for a one-shot (it has no
  next fire).
- **Never on a manual/replay failure** (an operator is watching). **Failure-isolated:** a throwing notifier is caught + logged and never fails the ledger write or bubbles out of the Quartz job — the
  run log is the truth, the alert a courtesy. **PII-free payload:** `JobFailureAlert` = jobKey / ownerModule / errorType / runId / timestamp — never the raw `ErrorMessage`.
- **Throttled per `JobKey`** (`IJobAlertThrottle` / `JobAlertThrottle`, in-process singleton like the concurrency gate): at most **one alert per job per hour**. Without it a frequently-scheduled job
  stuck in a failure state alerts on every fire — `reminders.scan` defaults to **every 5 minutes**, which with email default-on would mean hundreds of mails a day per Admin, and ignored alerts. ⚠️
  **Only the notification is throttled — never the ledger:** every failure is still written to `scheduled_job_run` and still logged, so a suppressed alert loses no information. Throttling is **per
  key**, so one noisy job can't mask a different job breaking. State is in-process, so it resets on restart (the first failure after a restart always alerts — the safe direction) and is per-process
  (correct on single-node).
- **Live consumer:** `Core.Notifications`'s `JobFailureNotifier` (auto-scanned `IScopedService`) delivers a
  `NotificationType.ScheduledJobFailed` in-app/push/email (default-on) to Admin + RootAdmin;
  `NoOpJobFailureNotifier` is the `TryAddScoped` fallback. Lineage links through the existing `ReplaysRunId`
  (no second linking column); the retention purge already excludes lineage-referenced rows.
- **~~Residual gap~~ — closed by the overdue sweep below (follow-up 08).** 05's alerting structurally covers only jobs that *run and fail*; a job that **never fires** produces no `Failed` run and
  never reaches
  `EmitFailureAlertAsync`. That is now detected actively rather than left pull-only.

### Overdue sweep — the "never fires" half (phase 08-followup — from 06/D4)

A Scheduler-owned recurring job (`OverdueJobSweepJobHandler`, dogfooding the substrate exactly like the retention purge) compares `NextRunAt` vs now for every `Active` job and pushes an alert for the
ones past their margin. It covers what 05 cannot: the scheduler process was down, the owning module's registrar never ran, the RAM-store trigger was lost on restart, or a `ScheduledJob` row outlived
the registration that created it. Registered via `SchedulerScheduledJobsRegistrar.BuildRegistrations(...)` alongside the purge.

- **One definition of overdue.** Reuses `OverduePolicy.WhereOverdue` — there is no second predicate. The **margin** is parameterized (display and alert want different cushions), and the alert path
  composes one extra, explicitly-named step: `OverduePolicy.WhereNotInFlight`.
- **⚠️ The running-job trap, and why a margin can't fix it.** `NextRunAt` is recomputed only **after** a handler returns (dispatcher step 8), so for its entire execution a job still advertises the
  fire it is *currently servicing*: a 4-minute body looks 4 minutes overdue while working perfectly. The pull dashboard can live with that (read next to `LastRunAt`, with an operator's context); an
  alert cannot, because it asserts **nothing is happening**. Widening the margin past the slowest job body is a guess that buys slow detection and breaks again the moment a job gets slower. The fix is
  a **fact, not an inference**:
    - `ScheduledJob.RunningSince` (`[AuditIgnore]`, nullable) is written by the dispatcher **before** it invokes the handler, in its own committed save — an in-process flag would be invisible to the
      sweep, which is the only reader that matters. It is cleared in step 8's existing save (so the common path costs no extra round trip) and again from a `finally` covering the early `DuplicateFire`
      return and any unexpected throw. Setting it is best-effort: a lost concurrency race is swallowed, because a control op must never stop a job from running, and a missing marker costs at most one
      spurious alert.
    - **Staleness bound.** A process killed mid-run leaves the marker set with nobody to clear it. Unbounded, that job would be permanently un-alertable — a **false negative**, the one failure mode a
      detector must not have. `WhereNotInFlight` therefore honours a marker only while it is younger than
      `OverdueJobSweep:MaxRunHours` (default **6 h**), so the condition self-heals. Chosen over a boot-time cleanup pass, which would have to race the scheduler starting.
    - With the marker exact, the alert margin drops to a **5-minute skew cushion**
      (`OverdueJobSweep:AlertMarginMinutes`) instead of a defensive 15 — better detection latency, no guessing.
    - The **dashboard deliberately keeps the simpler predicate** and still lists a running job as overdue. That is informative there and is pre-existing behaviour; only the alert path takes the
      exclusion.
    - Migration: `AddScheduledJobRunningSince` (HBCleaning + Sandbox), one nullable timestamp column.
- **Paused / Removed are excluded by construction, twice.** `WhereOverdue` requires `Status == Active` **and**
  a non-null `NextRunAt`, and `SchedulerService.Pause/RemoveJobAsync` also null `NextRunAt`. A job with no
  `NextRunAt` has no fire expectation and can never be late. Pinned by tests, not left to inspection.
- **Same seam, distinct type.** Alerts go through the **same** `IJobFailureNotifier` / `JobFailureAlert`
  (widened, never duplicated — `Core.Scheduler` still references no delivery module) but land as a **new**
  `NotificationType.ScheduledJobOverdue`: silence and an error are different problems with different fixes, and one inbox row for both makes triage harder. ⚠️ **The widening was real work, not a
  rename:**
  `JobFailureAlert.RunId` was `required long` and an overdue job has **no run row by definition**, so it is now
  `long?`; `FailedAtUtc` became `DetectedAtUtc` (honest for both kinds) and `ExpectedRunAtUtc` was added for the missed fire. The kind travels as an explicit `JobAlertKind`, **not** inferred from
  `RunId is null` — a consumer branching on the nullable id would mis-classify the day a third mode appears.
- **Throttled in its own bucket, on a longer window.** Key is `"overdue:{JobKey}"`, so an overdue alert and a failure alert for the *same* job never suppress one another. Window defaults to **12
  hours**
  (`AlertThrottleHours`), not the failure throttle's 1 hour, because the two conditions decay differently: a failing job re-alerts only when it re-fires, whereas an overdue job stays overdue
  **continuously** until a human fixes it — at 1h that would be 24 identical emails a day per Admin. Email is default-on for this type **because** the window is 12h; shortening one means revisiting
  the other.
- **Honours the per-job `AlertOnFailure` opt-out** — an owner who said "don't page me when this breaks" is not paged when it goes quiet either. **Failure-isolated per job**, so one undeliverable alert
  doesn't abandon the sweep. **`Enabled = false`** is a config kill switch that leaves the pull health view untouched.
- **Cadence** `OverdueJobSweep:CadenceMinutes`, default **every 10 minutes** (mirrors `ReminderScanOptions`). Cadence is only detection *latency* — the throttle owns re-alert frequency — so it stays
  simple and frequent. It must stay shorter than the alert margin, or a job could cross the margin and recover between two ticks.
- **⚠️ Its own blind spot is ACCEPTED, not solved (design Q1) — but the probe now exists.** If the sweep's own trigger is lost it detects nothing, including its own absence, and a dead process can't
  page anyone from inside itself: a self-healing detector is a contradiction. Only something **outside** the process can see scheduler-wide death, so the host now exposes what an external monitor
  needs:
    - **`/health/live`** — liveness, runs **no** checks (`Predicate = _ => false`), anonymous. A 200 means "this process is up and serving". Point your uptime monitor / container healthcheck here;
      when it stops answering, the scheduler is dead. ⚠️ **Never fold dependency checks into it** — an expired Graph secret would then page "scheduler down" for a perfectly healthy host, which is
      exactly the alert-noise failure this module keeps designing away from.
    - **`/health/ready`** — readiness, runs every registered check (today the Entra ID token acquisition).
    - Both anonymous by necessity (a monitor has no credentials) and safe: the default writer emits only the status word, never check names or exception detail. Mapped in
      `HBCleaning.AdminPortal/Program.cs`; the host also calls `AddHealthChecks()` itself so the mapping doesn't depend on the Graph integration.
    - Watching it is still **ops configuration** (the monitor, the alert route) — the app only exposes the seam. So the sweep detects *individual job* silence and the probe detects host death; neither
      substitutes for the other. Same class of accepted single-node limitation as the RAM job store (B4/B5) — **don't file it as a bug.** The sweep *does* cover a process that restarts
      half-registered: the registry rows survive in Postgres with their stale `NextRunAt`, so the first tick after recovery reports every job whose registration didn't come back.
- **No "recovered" notification (v1, design Q5).** A job that fires again silently stops being overdue — it drops out of the next sweep and out of the health view, and the admin who acted on the alert
  sees the status change there. A resolution message is an easy future add; shipping it now would double alert volume for a signal nobody asked for.
- **Tests:** `OverdueJobSweepJobHandlerTests` (alerts once with the Overdue kind + null `RunId` + the missed fire; `Paused`/`Removed`/no-`NextRunAt` never alert; a job that recovered gets no stale
  alert; a job inside the alert margin isn't paged though the dashboard would list it; **a running job is never alerted however long its body takes**; **a stale marker past `MaxRunHours` is ignored so
  a crash buys no permanent immunity**; the `AlertOnFailure` opt-out covers silence; three consecutive ticks alert once; the overdue bucket survives a same-job failure alert; kill switch; per-job
  failure isolation; registration contract).
  `ScheduledJobFailureAlertDispatcherTests` gains the marker's two halves — visible mid-run **read from a separate connection** (the point is that another process can see it), and cleared afterwards
  including when the handler throws. Plus the overdue case in `JobFailureNotifierLiveConsumerTests`, proving the live Notifications consumer maps it to its own type.

**Follow-up 06 policy review (2026-07-21) — three values 05 chose without owner sign-off, now reviewed:**

- **D1 throttle window — kept at 1 hour, no code change.** The window only matters for the 5-minute
  `reminders.scan`-class jobs; the monthly/yearly purges never re-fire inside an hour anyway, so widening it buys nothing for them and only slows detection for the fast-cadence case. Not made
  config-bindable — no deployment has asked to tune it, so `JobAlertThrottle.Window` stays a `static readonly` (the
  `ReminderScanOptions`-style binding is the escape hatch if one ever does).
- **D2 email default-on for `ScheduledJobFailed` — kept, explicitly contingent on D1.** The counter-argument (unbounded volume vs. every other default-on type being bounded-volume) is valid in
  isolation but is exactly what the throttle neutralizes. If D1's window is ever removed or shortened, D2 must be revisited in the same change.
- **D3 export columns — added.** `SchedulerExportService.ExportJobsOverview` now also carries `TimeZoneId`,
  `MaxRetries`, `AlertOnFailure`, `RunAtUtc` (appended after `LastOutcome`, so existing column indices are unchanged). The jobs-overview export is now a complete operational snapshot instead of an
  arbitrary subset. Covered by `SchedulerExportServiceTests.Csv_RendersOperationalColumns`.

**Misfire/catch-up:** the `MisfirePolicy`→Quartz instruction was set per-trigger in 02b; the behavioural default is `FireAndProceed` (a job down during its window catches up with a **single** fire,
not a storm). The run-log dedup is the DB-side safety net the Quartz reality check relies on — **no clustering / persistent store**; correctness lives in Postgres. Single-node is the documented
assumption; multi-node would be the trigger to add Quartz clustering + a persistent store + a DB-backed in-flight lock.

## Admin endpoints (phase 02b)

Thin Admin/RootAdmin-only wrappers over `IScheduler` + the registry reads, under the `/api/scheduled-job`
route prefix (`application/endpoint/scheduledJob/`): `RegisterJobEndpoint` (POST `/register`),
`RemoveJobEndpoint`/`PauseJobEndpoint`/`ResumeJobEndpoint`/`TriggerJobNowEndpoint` (POST by `JobKey`),
`GetByIdScheduledJobEndpoint` (GET `/{id}`), `GridScheduledJobEndpoint` (POST `/filtered-table`, filter by owner module / handler key / status / schedule type / next-run range). The in-process
`IScheduler` itself is **not** role-gated (trusted module/job code calls it directly).
> **Repo-convention note:** the build plan calls for FastEndpoints `Group`/`SubGroup` + `AutoTagOverride`,
> but **no module in this repo uses FE groups** (Inventory/Attendance use plain route prefixes). Per the
> orchestration's "repo wins" rule, these endpoints follow the existing route-prefix convention instead.

## Dashboard reads (phase 04a)

Pure projections over the phase-01/03 tables — **no new scheduling logic**. Admin/RootAdmin only, so the base
`ApplyUserScoping` no-op is safe (not widened). Routes use a `/api/scheduler-dashboard` prefix (repo-convention route prefixes, kept distinct from 02b's `/api/scheduled-job` diagnostic group) —
`application/endpoint/dashboard/read/`.
> **Partial coverage until phase 05 completes:** the dashboard (jobs-overview, run-history, health) only
> reflects jobs registered into the substrate via `RegisterRecurringJobAsync`. Legacy jobs still hand-wired
> directly in the host's `AddQuartz` block (see the migration tracker below) run **outside** this registry —
> they have no run-log rows, never appear in any dashboard list, and are invisible to the health view's
> failed/overdue/orphaned signals. Don't read "N scheduled jobs" on the dashboard as the full set of recurring
> work in the system until every row in the migration tracker is ✅.

- **`GetScheduledJobsOverviewEndpoint`** (POST `/jobs-overview`) — the dashboard jobs list. Reuses the phase-01
  `ScheduledJobDto` projection + the shared `BaseGridEndpoint`; adds over 02b's `ScheduledJobGridEndpoint` only a
  `LastOutcome` filter, the `OnlyOverdue` signal, and a stable default order (owner, then job key). Filtering + default sort live in the shared `application/dashboard/ScheduledJobsOverviewQuery` so
  the grid and its export can't drift.
- **`GetJobRunHistoryEndpoint`** (POST `/run-history`) — the append-only run log as a grid: filter by job (id or key snapshot), owner (via the `ScheduledJob` nav), outcome, trigger source, `StartedAt`
  range; **newest first** by default. Shared filter/sort in `application/dashboard/JobRunHistoryQuery`.
- **`GetJobRunByIdEndpoint`** (GET `/run/{id}`) — single-run detail. A plain endpoint (not `BaseGetByIdEndpoint`)
  because it needs **both** replay directions: `ReplaysRunId` (the run it replays) is on the row; the reverse (`ReplayedByRunIds`) is a second query for `ReplaysRunId == id` — keeps it a pure read
  with **no inverse navigation added to the entity**. This by-id view **does** surface `PayloadSnapshotJson` for inspection (the one place that does); payload PII hygiene is the handler's
  responsibility.
- **`GetSchedulerHealthEndpoint`** (GET `/health`) — the "needs attention" view: status counts, recent run outcomes over a **24h** window (straight off the run log — *the log is the source of truth*),
  and three actionable lists — `FailedJobs` (`LastOutcome == Failed`), `OverdueJobs`, and `OrphanedJobs` (a non-`Removed` registry row whose `HandlerKey` no longer resolves against the injected
  `IEnumerable<IScheduledJobHandler>`).
- **Overdue/stuck signal** (`application/dashboard/OverduePolicy`): an `Active` job whose `NextRunAt` is past
  `now − GraceMargin` has missed a fire. `GraceMargin = 60s` == the Quartz **default misfire threshold** (Scheduler sets no custom one — see `SchedulerQuartzConfig`); it absorbs the brief gap between
  a fire and the dispatcher writing the new `NextRunAt`, so a 1s-late job is **not** stuck but one well past the margin is (pinned by a worked test).
- **Exports** (`*/export?format=xlsx|csv`, default xlsx) reuse the grids' shared filter + default sort, no paging. Scheduler owns its **own** export stack (`application/export/` +
  `application/service/SchedulerExportService`,
  `ISingletonService`) mirroring `AttendanceExportService` (Syncfusion XlsIO for XLSX; semicolon CSV + UTF-8 BOM) — it can't use the Attendance one (that's a domain module). Technical infra columns
  only; the **run payload snapshot is never exported** (potential PII) even though the by-id detail shows it.
    - Both formats materialize the whole filtered set in memory and render synchronously on the request thread (no paging) — accepted for these Low-volume admin infra tables. If run-history ever grows
      large, the CSV path can be streamed row-by-row; XLSX stays in-memory (Syncfusion XlsIO has no row-streaming save) and would need a background-job + download-link approach instead.

## Operator controls & run-history replay (phase 04b)

The dashboard's **controls reuse the 02b endpoints** unchanged — trigger-now / pause / resume are the
`/api/scheduled-job/{trigger-now,pause,resume}` wrappers over `IScheduler` (no duplicate impl; the dashboard just calls them). 04b adds only **replay**:

- **`ReplayJobRunEndpoint`** (POST `/api/scheduler-dashboard/run/{id}/replay`, Admin/RootAdmin) — a thin wrapper over **`IScheduledRunReplayer`** (`infrastructure/ScheduledRunReplayer.cs`,
  `IScopedService`).
- **Replay re-runs a past run *through* the phase-03 dispatcher — never a forked execution path.** The replayer loads the original `ScheduledJobRun`, **validates** it (the run exists → 404; its
  `HandlerKeySnapshot` still resolves to a registered handler → else 409), then fires the **one durable dispatcher** off-schedule via the 02b `TriggerJob` path with a data map carrying
  `TriggerSource = Replay`, the original run's **`PayloadSnapshotJson`** as the payload override (so the replay reproduces what actually executed, even if the registry payload has since drifted), and
  `ReplaysRunId = original.Id`. The dispatcher does everything else — builds the context, applies the per-key gate, invokes the handler with failure isolation, and writes the **new, linked** run row.
  The endpoint/replayer build **no**
  `ScheduledJobContext` and write **no** run row.
- **Append-only preserved:** the original row is never mutated; the replay is a fresh row linked by
  `ReplaysRunId` (success or failure captured like any other run). Controls change registry state + Quartz triggers, never a past run row.
- **Dedup-safe by construction:** the replay fire carries `ScheduledFireTime = null` (like any off-schedule fire), so it is **not** matched by the dispatcher's scheduled-occurrence dedup and is never
  silently skipped against the original run.
- **Safety rails:** Admin/RootAdmin only; replay **respects `DisallowConcurrent`** — a replay overlapping a live run of the same `JobKey` is vetoed by the dispatcher's in-process gate (a `Vetoed` run
  row,
  `ErrorType = ConcurrencyVeto`), same as any fire. **Effect-idempotency is the handler's responsibility** — replaying a non-idempotent job repeats its side effects (surfaced as a warning in the
  contract doc + the frontend prompt).
- **Why a module-internal service, not the `Sydowwe.Framework.Contracts` contract:** replay is an operator/dashboard concern; owners register/control but never replay, so `IScheduledRunReplayer` stays in `Core.Scheduler`
  and the `Sydowwe.Framework.Contracts` `IScheduler` surface stays minimal.

**No frontend prompt or `.vue` views exist yet for the dashboard (04a reads + 04b actions) — the whole dashboard UI is unbuilt.** `frontend-prompts/scheduler-dashboard.md` was referenced here but
never written.

## Auditing decisions

- `ScheduledJobRun` is `[NoAudit]` — it is itself an append-only ledger (a self-audit), like Notification rows; auditing it would double-write.
- `ScheduledJob` stays audited (pause/resume/reschedule are meaningful edits), **but** the per-run observability columns `NextRunAt` / `LastRunAt` / `LastOutcome` are `[AuditIgnore]` — they're
  rewritten on every fire and the run log already captures each run. `Status` stays audited.
- Operator-initiated **trigger-now** and **replay** don't mutate the registry, so the CRUD interceptor sees nothing and the resulting `ScheduledJobRun` (`[NoAudit]`) carries no `UserId`. They're
  therefore captured as **business-audit events** from the endpoint — where the admin principal is present —
  `ScheduledJob.TriggeredNow` (`TriggerJobNowEndpoint`) and `ScheduledJob.Replayed`
  (`ReplayJobRunEndpoint`), so these privileged "fire arbitrary background work" acts stay attributable. Payloads are PII-free (jobKey/runId + initiating userId).
- **Run-log retention (audit 2026-07 L1/B3, decision D1):** the ledger is *not* kept forever —
  `PurgeExpiredRunLogsJobHandler` (`application/job/`, key `Scheduler.PurgeExpiredRunLogs`) hard-deletes
  `scheduled_job_run` rows monthly (GDPR Art. 5 (1)(e)), self-hosted on the substrate via
  `SchedulerScheduledJobsRegistrar` (`infrastructure/scheduling/`, wired by the HBCleaning host). A row is deleted only when **all three** hold: older than **3 years** (same horizon as `audit_log`),
  beyond its job's **keep-last-20** floor, and **not referenced by any `ReplaysRunId`** — the replay-lineage self-FK is `Restrict`, so referenced rows are excluded (chains age out over successive
  runs) rather than faulting the batch. `ExecuteDeleteAsync` is safe here precisely because the table is `[NoAudit]`. Registry rows (`ScheduledJob`) are never deleted by retention.

## Extension playbook

- **Register a recurring job (owner side):** implement `IScheduledJobHandler` (key it), then call
  `IScheduler.RegisterRecurringJobAsync` at startup with a `RecurringJobRegistration` (its `HandlerKey`
  matches your handler's `Key`). Idempotent — reconcile on every boot.
- **Add a stored field to the registry/run log:** add the property, configure it in the matching
  `*EntityConfiguration` with the `EntityBuilderExtensions` helpers (enums via `EnumColumn`, jsonb via
  `HasColumnType("jsonb")`), extend the DTO `Projection`, add a migration.

## Migrating existing jobs onto the substrate (phase 05)

The repo's hand-wired Quartz jobs move onto this substrate **one owning module per session/commit**, never a big-bang. Only the *wiring* moves — each job's **body stays in its owning module** (a
handler `using`s
`Sydowwe.Framework.Contracts.scheduling`, never `Core.Scheduler`).

> **Extra reason to finish this:** a legacy raw `IJob` does **not** get the dispatcher's ambient-scope
> guarantee (gotchas above), so a body that dispatches a FastEndpoints command needs a local
> `HttpContext ??= …` guard. `RolloverLeaveBalancesJob` carries one today for exactly this reason — **delete
> the guard when migrating it**, don't carry it into the handler.

**Recipe (per job):**

1. **Extract the body into a keyed handler.** `XxxJobHandler : IScheduledJobHandler, IScopedService` in the **same owning module**, `Key` = a stable `"<Module>.Xxx"`. Move the old `Execute` body
   verbatim into
   `ExecuteAsync(ScheduledJobContext, ct)` — same services, same logic. Keep a public method for the body so the job's existing unit/integration tests retarget by swapping the type name only.
   Auto-registered as an
   `IScheduledJobHandler` via the `IScopedService` scan (so the dispatcher's `IEnumerable<IScheduledJobHandler>`
   resolves it and registration's handler-key validation passes).
2. **Carry per-run inputs through the `Payload`** (read via `context.GetPayload<T>()`), not new domain coupling.
3. **Register at startup from the owning module.** A tiny `IHostedService` registrar in the owning module, wired by the host's DI extension (`AddCore` / `AddHbCleaning`), calls
   `IScheduler.RegisterRecurringJobAsync`
   for the jobs it owns — same `JobKey`/cron-or-interval, same `MisfirePolicy` intent,
   `[DisallowConcurrentExecution]` → `DisallowConcurrent = true`. Idempotent upsert → reconcile-on-boot is safe. **Guard:** the registrar **no-ops when the host hasn't wired the Quartz substrate**
   (probe
   `GetService<Quartz.ISchedulerFactory>() is null`) — a shared DI extension (`AddCore`) runs in portals with *and* without the substrate (the vanilla Sandbox has no `AddQuartz`), and the legacy jobs
   only ever ran in the HBCleaning host. Skipping there is behaviour-preserving, not a workaround.
4. **Remove the legacy wiring.** Delete the job's `q.AddJob<XxxJob>` + trigger from the host's `AddQuartz`
   block and delete the old `IJob` class. Confirm nothing else schedules it.
5. **Verify live** in the 04a/04b dashboard (right schedule + `NextRunAt`, trigger-now, run-log row).

**Behaviour-preserving only** — same schedule, misfire intent, concurrency flag, effect. A migration that changes *when/whether* a job runs is a bug. No new `AddQuartz`: by the end the host block
schedules nothing but the generic dispatcher.

**Worked example (done):** `Core.PurgeExpiredAuditLogsJob` → `PurgeExpiredAuditLogsJobHandler`
(`AdhdTimeOrganizer/application/job/`, key `Core.PurgeExpiredAuditLogs`, body unchanged) +
`CoreScheduledJobsRegistrar` (`AdhdTimeOrganizer/infrastructure/scheduling/`, wired in `AddCore`, monthly `0 0 3 1 * ?` UTC, `DisallowConcurrent`). Legacy `AddQuartz` lines removed from
`HbCleaningServiceExtensions`. Tests: the existing audit/partition tests retargeted to the handler;
`CoreScheduledJobsRegistrarTests` asserts the legacy schedule + idempotent reconcile + a single live trigger.

**Second migration (done):** `Core.PurgeExpiredSerilogLogsJob` → `PurgeExpiredSerilogLogsJobHandler` (key
`Core.PurgeExpiredSerilogLogs`, body unchanged, daily `0 15 3 * * ?` UTC, `DisallowConcurrent`), appended to
`CoreScheduledJobsRegistrar.Registrations`. It is the first migration to set the post-04b defaults **deliberately**: `MaxRetries = 0` (behaviour-preserving — the legacy raw `IJob` had no retry, and a
failed row-delete self-heals on the next nightly run) and `AlertOnFailure = true` (a real behaviour change, kept because a silently failing retention purge leaves PII-bearing log rows past their 30d
horizon forever).

**Third migration (done):** `Core.Attendance/MarkLeaveDoneBackgroundService` → `MarkLeaveDoneJobHandler`
(key `Attendance.MarkLeaveDone`, body unchanged, nightly `0 0 0 * * ?` UTC, `DisallowConcurrent`), registered by the module's own new `AttendanceScheduledJobsRegistrar`. Post-04b defaults set
deliberately:
`MaxRetries = 0` (behaviour-preserving — the work is idempotent catch-up, so a failed run's leaves are simply picked up by the next nightly run) and `AlertOnFailure = true` (behaviour change, kept — a
silently failing run leaves finished leaves stuck in `Approved`, skewing balances with nobody told).

It is also the **first migration of a job with a legacy `StartNow` trigger**. That boot catch-up is reproduced as a `TriggerNowAsync` after registration, gated by an opt-in
`AttendanceScheduledJobsRegistrar.TriggerOnBoot`
set so the module's yearly jobs never inherit it. `ScheduleSpec.RunOnceAt` is *not* the right tool here — its occurrence dedup makes a one-shot fire once ever, not once per boot — and the recurring
misfire cannot cover it either, since the RAM job store forgets missed fires across a restart. The boot fire is recorded as a
`Manual` run (neither retried nor alerted on), matching the legacy `StartNow` trigger.

**Fourth (and final) migration (done):** the three `Core.EmployeeModule` jobs, registered by the module's own new `EmployeeScheduledJobsRegistrar`. Bodies unchanged; schedules and `DisallowConcurrent`
preserved verbatim. The post-04b defaults were decided per job rather than per module, which is the point worth carrying forward:

- `Employee.AnonymizeTerminatedEmployees` (nightly `0 0 1 * * ?`) — `MaxRetries = 0`. Idempotent (`!IsAnonymized`), self-healing on the next nightly run, and the erasure is irreversible: retrying it
  fast buys nothing against a window measured in years. `AlertOnFailure = true` — a stalled Art. 17 purge means personal data is kept past its lawful window with nobody told.
- `Employee.NotifyUpcomingHrEvents` (weekly `0 0 6 ? * MON`) — `MaxRetries = 3`, a **deliberate improvement**. The digest's 7-day window is exactly one run wide, so a lost Monday can let a skúšobná
  doba / doba určitá / compliance expiry lapse unannounced; a duplicate digest is harmless by comparison.
- `Employee.RetryPendingStorageDeletions` (nightly `0 30 2 * * ?`) — `MaxRetries = 0`, because **this job is itself a retry loop**. Its `AttemptCount`/`MaxAttempts` ladder (10 attempts, one per
  nightly run) *is* the policy; dispatcher backoff on top would burn several rungs off it within minutes of one outage and reach
  `GaveUp` — and its Critical "manual SharePoint deletion required" escalation — far sooner than intended. It is also the module's `StartNow` job: boot catch-up reproduced via the opt-in
  `TriggerOnBoot` set.

A note that generalises the "must start throwing" rule from the Attendance migration: **a body that swallows a failure is only wrong when the failure is its own.** `RetryPendingStorageDeletions`
deliberately does *not*
throw when `IDocumentStorage.DeleteAsync` fails — a storage failure is this job's normal *input*, recorded on the entry and retried next run, so the fire is genuinely a success. Only a real
infrastructure fault throws. That is the opposite shape to `RolloverLeaveBalancesJob`, which was hiding its own failure; check which one you have before "fixing" it.

**Migration tracker** — ✅ **COMPLETE.** All 8 legacy jobs now enter through `IScheduler`; the HBCleaning
`AddQuartz` block calls nothing but `AddSchedulerQuartzDefaults()` (the generic dispatcher), and a comment there records that nothing may be hand-wired into it again. | Owning module | Job |
Status | |---|---|---| | `Core` | `PurgeExpiredAuditLogsJob` → `PurgeExpiredAuditLogsJobHandler` | ✅ done | | `Core` | `PurgeExpiredSerilogLogsJob` → `PurgeExpiredSerilogLogsJobHandler` | ✅ done | |
`Core.EmployeeModule` | `AnonymizeTerminatedEmployeesJob` → `AnonymizeTerminatedEmployeesJobHandler` | ✅ done | | `Core.EmployeeModule` | `NotifyUpcomingHrEventsJob` →
`NotifyUpcomingHrEventsJobHandler` | ✅ done | | `Core.EmployeeModule` | `RetryPendingStorageDeletionsJob` → `RetryPendingStorageDeletionsJobHandler` (had a startup `StartNow` trigger) | ✅ done | |
`Core.Attendance` | `MarkLeaveDoneBackgroundService` → `MarkLeaveDoneJobHandler` | ✅ done | | `Core.Attendance` | `RolloverLeaveBalancesJob` → `RolloverLeaveBalancesJobHandler` | ✅ done | |
`Core.Attendance` | `ProvisionNextYearAttendanceJob` → `ProvisionNextYearAttendanceJobHandler` | ✅ done |

The two **yearly** Attendance jobs (done together, since they share every decision) are the first migrations to set **`MaxRetries = 3`** — a *deliberate improvement*, not behaviour-preservation. On a
yearly schedule
"no retry" means a transient failure waits a full year; both bodies are idempotent (existing balances / already-seeded years are skipped), so a re-fire is safe. `AlertOnFailure` stays on for the same
reason: after the final retry the next scheduled attempt is twelve months away. Two other notes from that migration:

- **A body that swallowed its own failure had to start throwing.** `RolloverLeaveBalancesJob` logged the command's `Result.Failed` and returned, so the fire counted as a success — which would have
  left
  `MaxRetries` and `AlertOnFailure` as dead configuration. The handler now throws. Check every migrated body for the same shape.
- **Time zone stays UTC.** These are the calendar-boundary jobs follow-up 01 exists for, but both sit far from a boundary by design (00:30 on Jan **2nd**; Dec 1st for a year seeded a month ahead), so
  `null` ⇒ UTC preserves the legacy fire instant with nothing at risk. Switching to `Europe/Bratislava` remains a separate, deliberate change.
- The follow-up 07 ambient-scope guard `RolloverLeaveBalancesJob` carried was **deleted, not copied** — the dispatcher now guarantees it for every fire.

> Both jobs that carried a legacy startup `StartNow` trigger (run-once-on-boot **plus** the recurring
> schedule) reproduce it the same way: a boot `TriggerNowAsync` gated by the registrar's opt-in `TriggerOnBoot`
> set. `RegisterRecurringJobAsync` only creates the recurring trigger, and `ScheduleSpec.RunOnceAt` is the
> wrong tool (it fires once *ever*, not once per boot).

## Deeper reference

- `domain-map.md` — model, invariants, the `Sydowwe.Framework.Contracts` surface, file index.
- Build plan: phases 01–05 all done → `prompts/_done/scheduler/`. Audit follow-ups 01–08:
  `prompts/_done-followups/scheduler-followups/`.
