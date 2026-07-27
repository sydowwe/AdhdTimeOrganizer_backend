# Reminders — Agent Summary

> **Status:** complete. All build phases are done — 01a (the Kernel contract), 01b (entities + EF config +
> migration), 01c (read-DTO projections), 02a (the registry implementation + command endpoints), 02b
> (the admin read/inspector endpoints), 03a (the scan handler), 03b (registering the scan with the
> Scheduler module), 04a (dispatch policy: per-kind opt-out + quiet hours), 04b (digest batching), 05a
> (dashboard reads + exports) and 05b (per-recipient snooze/dismiss + the dashboard frontend prompt). The
> build plan lives in `prompts/reminders/`.

> **Docs convention: summary-only.** This is an infra module — its invariants and business rules live
> inline here (see *Decisions baked into the config*, *Invariants*, and the per-phase sections), **not** in a
> separate `domain-map.md`. There is deliberately no `domain-map.md`; this summary is the single oracle.

**Purpose:** The vanilla-core, always-on infrastructure that owns *when a notification fires on a schedule* — a deadline/reminder registry, a recurring scan that finds due occurrences, and a dispatch
policy that hands the actual send to the Notification module.

**Bounded context:** Owns *when to send, on a schedule* — the reminder registry, recurrence, the scan logic, the dispatch policy, and the append-only dispatch log. Does **NOT** own
transport/channels/text (Notifications) or the Quartz substrate + run log (Scheduler). "React to a domain event" is explicitly out of scope — this is the time-based side only.

## The three-module split (do not blur it)

- **Notifications** (`Core.Notifications`) = *how/where to send* — transport (SignalR + Web Push), channels, per-user preferences, the text renderer, delivery/history. Reached only through
  `INotificationService.NotifyAsync(...)`.
- **Scheduler** (`Core.Scheduler`) = *the generic time substrate* — Quartz config, the recurring-job registry, the append-only run log, the keyed dispatcher. Reminders registers its single scan job
  here via `IScheduler` + an `IScheduledJobHandler`; it never calls `AddQuartz` or owns a Quartz `IJob`.
- **Reminders** (this) = *when to send, on a schedule* — the consumer that sits on top of both.

> **Crisp rule:** this module imports **no domain module**. The dependency arrow always points *into*
> the Kernel contract: owning module → `Kernel.reminders` ← this module. If you ever want
> `using AdhdTimeOrganizer.<SomeDomain>` here, the design is wrong — stop and flag it. (A guard
> test, `ReminderContractGuardTests`, pins the contract domain-free.)

## Dependency seams

- **Exposes:** the `Kernel.reminders` contract (table below). Owning modules depend on the Kernel, never on this module.
- **Consumes:** the Notification module **only** through `Kernel.notification`
  (`INotificationService.NotifyAsync` to send, and `IQuietHoursReader` to read the per-user quiet-hours window Notifications now owns); the Scheduler module **only** through `Kernel.scheduling`
  (`IScheduler` + `IScheduledJobHandler`, phase 03). Nothing domain-specific.

## The Kernel contract surface (phase 01a — `MojaDigitalnaFirma.Kernel/reminders/`)

The only thing owning modules reference. Mirrors how `INotificationService` lives in `Kernel.notification`.

| Type                         | Kind                        | Role                                                                                                                                                                                           |
|------------------------------|-----------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `IReminderRegistry`          | interface                   | Registration/control seam: `RegisterAsync` (idempotent upsert by key) · `CancelAsync` · `PauseAsync` · `ResumeAsync`. Impl in phase 02.                                                        |
| `ReminderKey`                | record                      | The idempotency identity: `(OwnerModule, SubjectType, SubjectId[string], Kind)`. Structural equality.                                                                                          |
| `ReminderSchedule`           | sealed class                | When a reminder occurs. Factory methods `OneShot` (DueAt + lead-offset minutes) · `RecurringInterval` (preset + anchor + optional end) · `RecurringCron` (cron UTC + optional end).            |
| `ReminderRegistration`       | sealed class                | The upsert payload: key, schedule, `TemplateKey`, `Payload`, optional `NotificationType`, `RecipientMode` + (explicit user ids \| resolver key), + phase-04 hints `DigestKey` / `ChannelHint`. |
| `ReminderRegistrationResult` | record                      | Upsert outcome: `DefinitionId`, `Key`, `Created` (insert vs update-in-place).                                                                                                                  |
| `IReminderRecipientResolver` | interface (keyed)           | The **owner** implements it to resolve recipients at dispatch time (`Key` ↔ `RecipientResolverKey`). This module never encodes the domain rule.                                                |
| `ReminderResolutionContext`  | record                      | Passed to the resolver: key, template key, payload, occurrence instant.                                                                                                                        |
| `IReminderRenderer`          | interface (keyed, optional) | The **owner** may implement it to map a reminder into a `RenderedReminder` when text isn't a plain `NotificationType` (`TemplateKey` ↔ `TemplateKey`).                                         |
| `RenderedReminder`           | record                      | A renderer's result: `(NotificationType Type, object? Payload)`.                                                                                                                               |
| `ReminderRenderContext`      | record                      | Passed to the renderer: key, template key, payload, occurrence instant.                                                                                                                        |
| `ScheduleType`               | enum                        | `OneShot` · `RecurringInterval` · `RecurringCron`. (Distinct from `Kernel.scheduling.ScheduleType`.)                                                                                           |
| `ReminderIntervalPreset`     | enum                        | `Daily` · `Weekly` · `Monthly` · `Quarterly` · `Yearly` — calendar cadence (coarser than the scheduling one).                                                                                  |
| `RecipientMode`              | enum                        | `ExplicitUsers` · `ResolverStrategy`.                                                                                                                                                          |

### Text resolution — one of two paths (the renderer → `NotifyAsync` seam)

A reminder carries a required `TemplateKey` + JSON `Payload`. Text comes from **exactly one** of:

1. the registration's `NotificationType` — a plain Notification type the Notification module renders from
   `Payload`; or
2. an `IReminderRenderer` the owner registers (keyed by `TemplateKey`), which maps the occurrence into a
   `RenderedReminder` (a `NotificationType` + payload) the Notification module renders.

There is **no** separate `TemplateKey`→content mapping table. Dispatch ultimately only has
`INotificationService.NotifyAsync(recipients, NotificationType, payload)` — there is **no** transport path for free-form pre-rendered title/body, so a renderer must resolve to a
`(NotificationType, payload)` pair. A reminder that genuinely needs free-form text is a generic/passthrough `NotificationType` to add in the Notification module — not a new transport path here.

- **Phase 02 validates** at registration that at least one path is satisfiable (`NotificationType` set **or** a renderer registered for `TemplateKey`); `TemplateKey` is always required.
- **Phase 03 resolves** at dispatch with a fixed precedence: `NotificationType` wins (checked first); the keyed `IReminderRenderer` is the fallback, used only when `NotificationType` is null (a
  renderer may be registered *after* a registration that set `NotificationType`, so both can coexist at dispatch). If neither resolves to a `(NotificationType, payload)` pair, that occurrence is a
  **logged dispatch failure**, never a silent skip.

## Idempotency & discipline (the contract expectation for owners)

- `RegisterAsync` is an **idempotent upsert** keyed by `ReminderKey`'s 4-tuple. Re-registering the same key updates in place; it never duplicates.
- Owners **re-register on a date change** and **cancel on entity deletion** (outbox-like discipline).

## Persistence model (phase 01b + 05b — `Core.Reminders/domain/`)

`BaseTableEntity` descendants (module-owned infra, **not** user-scoped — a background scan/registrar with no authenticated user writes them, so they avoid the `BaseEntityWithUser` `UserId == 0` FK
trap). All instant columns are `DateTimeOffset` → **`timestamptz`** (reminders reason about absolute instants; the Kernel `ReminderSchedule` already uses `DateTimeOffset`). Enums persist **as
strings** via
`EnumColumn`, so a new `ReminderScheduleType` / `ReminderIntervalPreset` value needs no migration.

| Entity                     | Role                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
|----------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `ReminderDefinition`       | One row per idempotency key. Key columns `OwnerModule` / `SubjectType` / `SubjectId` (string) / `Kind`; content `TemplateKey` + `PayloadJson` (jsonb) + optional `NotificationType`; schedule `ScheduleType` + `DueAt` / `IntervalPreset` / `Cron` / `AnchorDate` / `EndsAt`; recipients `RecipientMode` + `RecipientResolverKey` + the `Recipients` child collection; one-shot `LeadOffsets` child collection; scanner state `Status` / `NextOccurrenceAt` / `LastOccurrenceAt` / `CompletedAt`; phase-04 placeholders `DigestKey` / `ChannelHint`; `IsActive` (`ISoftDeletable`). |
| `ReminderRecipient`        | Explicit recipient (`ExplicitUsers` mode). `ReminderDefinitionId` FK (**Cascade**) + a plain indexed `UserId`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `ReminderLeadOffset`       | One-shot lead-time offset. `ReminderDefinitionId` FK (**Cascade**) + `OffsetMinutes` (negative = before `DueAt`, 0 = at it).                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| `ReminderDispatch`         | The append-only dispatch ledger. `ReminderDefinitionId` FK (**Restrict**), `OccurrenceAt`, `DispatchedAt`, `Outcome`, `SkipReason?`, `NotificationTypeSnapshot?`, `TemplateKeySnapshot?`, `RecipientsSnapshot` (jsonb, **ids only — no PII**), `CorrelationId`, self-FK `ReversesDispatchId` (**Restrict**), and `ReminderOccurrenceActionId?` FK (**Restrict**, phase 05b) — the marker-state dedup link set only on a snoozed re-delivery.                                                                                                                                        |
| `ReminderOccurrenceAction` | The append-only per-recipient snooze/dismiss ledger (phase 05b). `ReminderDefinitionId` FK (**Restrict**), `OccurrenceAt`, plain `UserId` (no FK), `ActionType`, `SnoozeUntil?`, `ActedAt`, self-FK `ReversesActionId` (**Restrict**). `[NoAudit]`. Indexes: `(ReminderDefinitionId, OccurrenceAt, UserId)` for the resolve-recipients drop, `(ActionType, SnoozeUntil)` for the due-snoozes source.                                                                                                                                                                                |

**Runtime enums** (`domain/enum/` — internal stored state, **not** the Kernel contract):
`ReminderStatus { Active, Paused, Cancelled, Completed }` ·
`DispatchOutcome { Sent, Skipped, Failed, Reversed }` ·
`SkipReason { AlreadyDispatched, QuietHours, NoRecipients, OptedOut, Dismissed, Snoozed, Cancelled, Other }`
(three are **reserved** — `AlreadyDispatched` / `QuietHours` / `Cancelled` are never persisted by the current scanner; those skips happen silently with no row — see the enum's XML docs) ·
`ReminderActionType { Snooze, Dismiss, Reversal }` (phase 05b). (`ReminderScheduleType` / `ReminderIntervalPreset` / `RecipientMode` come from `Kernel.reminders`, not redefined here.)

**Decisions baked into the config:**

- **Idempotency is a DB constraint** — a **unique composite index** on
  `(OwnerModule, SubjectType, SubjectId, Kind)`, deliberately **non-filtered** so a `Cancelled`/`Completed`
  row still occupies the key; re-registering a cancelled key resolves to the *same* row and reactivates it in place (phase 02), never colliding. A second **scanner index** on
  `(Status, NextOccurrenceAt)` makes the phase-03 scan (`Status = Active AND NextOccurrenceAt <= now`) a range seek.
- **`ReminderDispatch` is append-only and `[NoAudit]`** — like `ScheduledJobRun` / the Notification rows, a ledger is self-auditing, so auditing it would double-write. Rows are inserted, never
  updated/deleted; corrections are `Reversed` rows linked by `ReversesDispatchId`. Its FK to the definition is **Restrict** so deleting a definition can't silently erase dispatch history; the dedup
  index `(ReminderDefinitionId,
  OccurrenceAt)` powers the scanner's "already dispatched this occurrence?" check (phase 03).
  `ReminderDefinition` and the two child tables stay audited (default).
- **`ReminderRecipient.UserId` has no FK to the user table** — the orchestration requires recipient user ids to be validated through the user module's query surface at registration, not
  duplicated/coupled here. It is a plain indexed column; the only relationship is the owning `ReminderDefinitionId`.

The DbSets + `ApplyConfigurationsFromAssembly(typeof(ReminderDefinition).Assembly)` are wired in
`AdhdTimeOrganizer/infrastructure/persistence/AppCoreDbContext.cs`; the migration (`RemindersModule`) is generated into both deployment portals (`…AdminPortal.Sandbox` and
`…HBCleaning.AdminPortal`), matching the Scheduler module's per-deployment migration pattern.

## Read-DTO surface (phase 01c — `Core.Reminders/application/dto/`)

SQL-translatable read models the later phases compose — the registry inspector (02b) and the dashboard / dispatch history (05). Each implements `IProjectionResponse<TDto, TEntity>` with a static
`Projection(IQueryable<TEntity>)` (the canonical repo pattern, mirroring Scheduler's `ScheduledJobDto` and Inventory's `StockMovementResponse`); child collections project inline via a nested
`.Select(...).ToList()`. Reads are `AsNoTracking`, no in-memory mapping / Mapperly.

| DTO                     | Projects             | Notes                                                                                                                                                                                                                                                   |
|-------------------------|----------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `ReminderDefinitionDto` | `ReminderDefinition` | Key, content (`PayloadJson` included), full schedule, recipient mode + resolver key, scanner state (`Status` / `NextOccurrenceAt` / `LastOccurrenceAt` / `CompletedAt`), `DigestKey` / `ChannelHint`, `IsActive`, plus the two child collections below. |
| `ReminderRecipientDto`  | `ReminderRecipient`  | `UserId` only (no PII). Ordered by `UserId`. Nested child of the definition DTO.                                                                                                                                                                        |
| `ReminderLeadOffsetDto` | `ReminderLeadOffset` | `OffsetMinutes`. Ordered by `OffsetMinutes`. Nested child of the definition DTO.                                                                                                                                                                        |
| `ReminderDispatchDto`   | `ReminderDispatch`   | Ledger snapshot fields + `Outcome` / `SkipReason` / snapshots / `RecipientsSnapshot` (jsonb) / `CorrelationId` / `ReversesDispatchId`.                                                                                                                  |

## Registry & command endpoints (phase 02a — `Core.Reminders/infrastructure/` + `…/application/endpoint/`)

The in-process registration seam owners call, plus the thin Admin control endpoints.

**`ReminderRegistryService` (`infrastructure/`, `IReminderRegistry`, `IScopedService`)** — the only writer of
`ReminderDefinition`. Auto-registered by the blanket `IScopedService` scan (the Reminders assembly is force-loaded in `MojaDigitalnaFirma.AdminPortal/config/CoreServiceExtensions.cs` alongside
Notifications / Inventory), and **safe with no authenticated user** — a background registrar inserts the plain-table entity without the `UserId == 0` FK trap.

- **`RegisterAsync` is an idempotent upsert** keyed by the `ReminderKey` 4-tuple: no row ⇒ insert (`Status = Active`); a row exists ⇒ update schedule / template / payload / recipients **in place**
  (child collections replaced wholesale) and recompute `NextOccurrenceAt`. Returns `ReminderRegistrationResult`
  (`Created` = insert vs update). Returns the same id on re-register — never a duplicate.
- **Reactivation on update:** a `Cancelled`/`Completed` row goes back to `Active` (and `CompletedAt`
  cleared); a `Paused` row **stays `Paused`** with `NextOccurrenceAt` left null (preserve the admin pause —
  `ResumeAsync` reactivates it), skipping the recompute.
- **`CancelAsync` / `PauseAsync` / `ResumeAsync`** are all idempotent. Cancel ⇒ `Cancelled` + clear
  `NextOccurrenceAt` (no-op on an unknown key — owners cancel on deletion blind to prior state). Pause:
  `Active → Paused` + clear next. Resume: `Paused → Active` + recompute. Non-matching states are no-ops.
- **Validation at registration (fail fast, `ArgumentException` → 400):** `TemplateKey` required; per
  `ScheduleType` (one-shot needs `DueAt`, offsets unique and ≤ 0; interval needs preset + anchor, `EndsAt`
  after anchor; cron must be valid); recipients (explicit list non-empty, or a resolver key matching a **registered** `IReminderRecipientResolver`); text source (a `NotificationType` **or** a
  registered
  `IReminderRenderer` for the `TemplateKey` — both may coexist, only "neither" is rejected).

**Owner discipline (the contract expectation):** owners **re-register on a date change** (same key, new schedule) and **cancel on entity deletion**. This module never reaches into the owner to detect
either — it only reacts to these declarative calls (outbox-like).

**The shared next-occurrence helper** — `domain/service/ReminderOccurrenceCalculator` is the single source of truth for "what occurs next": the soonest occurrence **at or after now** not already in
the dispatch log and not past a recurring schedule's end. Past occurrences are skipped (no backfill flood). **Phase 03's scanner reuses this exact helper** to advance recurring reminders, so
register/resume and scan agree by construction.

**Command endpoints** (`application/endpoint/reminderDefinition/command/`, Admin/RootAdmin) wrap the registry:
`RegisterReminderEndpoint` (POST `/reminder-definition/register`, returns the result; checks per-type required fields the flat→typed request can't express, then delegates), `Cancel` / `Pause` /
`ResumeReminderEndpoint` (POST `…/cancel|pause|resume`, 204, keyed by `ReminderKeyRequest`).

**Two flagged decisions (repo wins over the prompt):**

- **Endpoint grouping/tagging.** The 02a prompt asked for FastEndpoints `Group "Reminders"` / `SubGroup
  "Registry"` / `AutoTagOverride`, but **no module in this repo uses `Group`/`SubGroup`/`AutoTagOverride`** — endpoints use a route prefix and Swagger auto-tags by its first segment (e.g. Scheduler's
  `scheduled-job`). Followed the repo: routes are prefixed `/reminder-definition/…` (auto-tag `reminder-definition`), shared by the 02b reads.
- **Cron-evaluator dependency.** `RecurringCron` occurrence math needs a cron validate + next-fire helper. The prompt's preferred path is "use a Scheduler- **contract** cron helper", which didn't
  exist. Rather than pull a second Quartz dependency into this module (or reference `Core.Scheduler` directly — forbidden by the dependency rule), added **`ICronEvaluator` to `Kernel.scheduling`**,
  implemented by **`CronEvaluator` in
  `Core.Scheduler`** (the single Quartz owner, over `CronExpression`; stateless `ISingletonService`, no
  `ISchedulerFactory` so it's safe under the scan in Quartz-less hosts). Reminders consumes it through the contract only — Quartz stays out of this module, the arrow still points into the Kernel. This
  is *internal per-occurrence* cron, distinct from the *scan-cadence* cron handed to Scheduler in phase 03.

## Registry inspector — read endpoints (phase 02b — `…/application/endpoint/reminderDefinition/query/`)

The admin read/inspector surface over registered reminders — boilerplate over the repo's read bases, no new business logic. Both Admin/RootAdmin only (infra views), both reuse the 01c
`ReminderDefinitionDto.Projection` (`AsNoTracking`, recipients + lead offsets projected inline).

- **`GetReminderByIdEndpoint`** (GET `/reminder-definition/{id}`) — one definition with its recipients + lead offsets. Extends `BaseGetByIdEndpoint`; `AuthorizeAsync` returns true (admin-only, no row
  scoping).
- **`ReminderDefinitionGridEndpoint`** (POST `/reminder-definition/filtered-table`) — paged/filtered/sorted. Extends `BaseGridEndpoint`; `ApplyCustomFiltering` over `ReminderDefinitionFilterRequest`
  (owner module, subject type, kind, status, schedule type, next-occurrence range). The base `ApplyUserScoping` no-op is correct — Admin-only, so do **not** widen `AllowedRoles()` without overriding
  it (the no-op returns all rows). The per-recipient dashboard is phase 05.

These share the `/reminder-definition/…` route prefix (auto-tag `reminder-definition`) with the 02a command endpoints. Tests: `…/integration/reminders/GetReminderByIdEndpointTests.cs` +
`ReminderDefinitionGridEndpointTests.cs` (the matching base test classes + per-column filter/sort scenarios).

## The scan handler — the dispatch engine (phase 03a — `Core.Reminders/application/job/`)

`ReminderScanJobHandler` is the algorithm that turns the registry into actual sends. It is a **keyed
`IScheduledJobHandler`** (`Key = "reminders.scan"`, the `HandlerKey` constant), **not** a Quartz `IJob`:
03b registers it as the single recurring scan job with the Scheduler module — this module never calls
`AddQuartz`. Auto-registered via `IScopedService`. Background-safe (no authenticated user). Dispatch goes **only** through `INotificationService.NotifyAsync` (the Notification contract); recipients +
text come **only** through the owner's Kernel strategies (`IReminderRecipientResolver` / `IReminderRenderer`, matched by `.Key` / `.TemplateKey` over the injected `IEnumerable<>`) — no domain imports.

**"Now" is the fire instant** (`ScheduledJobContext.ActualFireTimeUtc`), not `DateTimeOffset.UtcNow`, so the scan is deterministic and a multi-fire lifecycle can be driven at controlled instants in
tests.

**Per run:**

1. Seek due `Active` reminders: `Status = Active AND NextOccurrenceAt != null AND NextOccurrenceAt <= now`, ordered, bounded (`ScanBatchSize = 500`). Only ids are read; each is reloaded (with
   recipients + lead offsets) inside the loop and re-checked against the predicate (it may have been paused/cancelled/advanced since the seek). Failure isolation: an unexpected error on one reminder
   logs + `ChangeTracker.Clear()` and the batch continues; reloading per id means the clear can't detach the rest of the batch.
2. **Enumerate due occurrences** (`<= now`, not already dispatched) by walking the shared
   `ReminderOccurrenceCalculator` with a growing skip set.
3. **Dedup** is the `LoadEffectiveOccurrencesAsync` set — the `OccurrenceAt` of every **effective** dispatch row (outcome ≠ `Reversed` **and** not itself reversed by a later correction row). An
   occurrence in this set is skipped, so a re-run / misfire / overlap **never double-sends**. The reversal exclusion is forward-compat for phase 05's snooze/dismiss (phase 03 writes no reversals).
   4–7. For the chosen occurrence: resolve recipients (`ExplicitUsers` → the stored `ReminderRecipient` rows;
   `ResolverStrategy` → the keyed resolver), resolve text (**fixed precedence: a set `NotificationType` wins**, even if a renderer also exists for the `TemplateKey`; the keyed `IReminderRenderer` is
   the fallback only when
   `NotificationType` is null), `NotifyAsync`, then append **one** terminal `ReminderDispatch` row (`Sent` / `Skipped` / `Failed`, recipient-id snapshot, `CorrelationId`). The stored `PayloadJson` is
   passed as a `JsonNode` so it re-serialises to the exact registered payload the renderer reads.
8. **Advance** `NextOccurrenceAt` via the same calculator over the full dispatched set; no remaining occurrence ⇒ `Status = Completed` + `CompletedAt` + null `NextOccurrenceAt`.

**Pinned catch-up default (documented, not invented):** after downtime several instants may be due at once. The scan acts on the **single most-recent due, undispatched occurrence per reminder per
run** and advances
`NextOccurrenceAt` past all older missed instants — each older instant is collapsed to a `Skipped` / `Other`
row for audit — so the next scan can't re-pick them. **One send per reminder per scan; no stale storm.** The Quartz-level misfire policy is set on the registration in 03b; this is the per-occurrence
catch-up Reminders owns.

**Failure handling (all logged, never silent, never aborting the batch):** missing strategy at dispatch time → `Failed`; empty recipients → `Skipped` / `NoRecipients`; neither `NotificationType` nor a
renderer resolves → `Failed` (never a silent skip); a resolver / renderer / `NotifyAsync` throw → `Failed` + the schedule still advances past the occurrence (a non-reversed `Failed` row means no
automatic retry — re-dispatch would be a phase-05 reversal).

> **Concurrency caveat:** the dedup is a check-then-insert, race-free **only** because 03b registers the job
> with `DisallowConcurrent` (no two scans overlap; sequential re-runs see the prior run's committed rows). The
> `(ReminderDefinitionId, OccurrenceAt)` index supports the lookup but does not itself prevent a concurrent
> double-insert. Do not run the scan concurrently without a stronger constraint.
>
> **Scan-vs-registry:** `DisallowConcurrent` only serialises scan-vs-scan — the admin registry
> (`ReminderRegistryService` cancel/pause/resume/re-register) runs on the request thread and can bump a
> definition's `row_version` while a scan holds that row mid-flight. Since `NotifyAsync` fires *before* the
> dispatch save, a naïve `ChangeTracker.Clear()` on the resulting `DbUpdateConcurrencyException` would drop the
> ledger row and let the next scan re-send. `SaveDispatchAsync` (used by the per-occurrence and digest flush
> saves) instead catches the conflict, detaches the conflicting (stale) definition update — the admin action
> wins the definition — and re-commits the append-only `ReminderDispatch` insert (s) on their own, so the
> occurrence stays deduped and is never re-sent. The detach+retry **loops**: a digest flush touches several
> definitions and more than one may have raced, so each retry can surface a fresh conflict on a different row;
> it detaches and retries until the save commits clean (every iteration removes ≥1 conflicting row, so it
> converges within the batch size — a `MaxConcurrencyRetries` cap is only a runaway safety net).

Tests: `…/integration/reminders/ReminderScanJobHandlerTests.cs` (drive `ExecuteAsync` directly against the real Postgres log with a recording/failing `INotificationService`): each due one-shot offset
dispatches once then completes; a recurring reminder dispatches + advances; a second scan over the same window is a no-op (dedup); catch-up over ≥2 missed fires sends only the most recent + advances
past all; explicit vs resolver recipients; empty recipients → `Skipped`; text precedence; neither source → `Failed`; resolver / `NotifyAsync`
failure → `Failed` + batch continues; `Paused` / `Cancelled` / `Completed` never picked up.

## Registering the scan with the Scheduler module (phase 03b — `infrastructure/scheduling/`)

The 03a handler is inert until something fires it. **Reminders owns no Quartz** — it never calls `AddQuartz`, owns no Quartz `IJob`, and references no Quartz assembly (guarded by
`RemindersNoQuartzGuardTests`). It registers its recurring scan as a job with the Scheduler module through the `Kernel.scheduling`
contract; all Quartz infra, the misfire→instruction mapping and the run log live in Scheduler. (The registrar later gained a second entry — the retention purge; see "Ledger retention" below. Still
exactly **one trigger per module-wide job**, never one per reminder.)

- **`RemindersScheduledJobsRegistrar` (`IHostedService`)** — the owner-side reconciliation seam, mirroring Scheduler phase-05's `CoreScheduledJobsRegistrar`. On boot it calls
  `IScheduler.RegisterRecurringJobAsync(BuildRegistration(options))`: `JobKey`/`HandlerKey` both
  `reminders.scan` (the 03a `ReminderScanJobHandler.HandlerKey`), `OwnerModule = "Reminders"`,
  `DisallowConcurrent = true`, the configured cadence + misfire policy. Idempotent upsert by `JobKey` — re-registering every boot converges registry + trigger with no duplicates, and is **required**
  (Scheduler's RAM job store drops all triggers on restart). Resolve+register is wrapped in a try/catch (LogError) so a misconfigured cadence or absent substrate logs and skips, never crashing host
  startup. `BuildRegistration` is a static so the registration is asserted directly in tests.
- **Wiring.** Registered as a hosted service in `HbCleaningServiceExtensions.AddHbCleaning` (the host that owns the Quartz substrate), **not** the shared `AddCore` — because Reminders references no
  Quartz it can't self-guard on `ISchedulerFactory` the way `CoreScheduledJobsRegistrar` does, and the vanilla Sandbox host wires no scheduler at all (nothing to register there). Options bound from
  the `ReminderScan` config section in the HBCleaning `Program.cs` (alongside `PushNotificationOptions`).
- **`ReminderScanOptions`** — the configurable cadence. **Default: every 5 minutes** (`CadenceMinutes = 5` →
  `ScheduleSpec.Every(Minute, 5)`); set `Cron` to override with a Quartz cron (UTC). The cadence is the **firing-precision floor**: a reminder fires within one cadence of its instant — 5 min is ample
  for deadline / lead-offset reminders and cheap on a small-company single node. This *scan-cadence* schedule is **distinct** from a reminder's *internal* recurrence (the registry's `RecurringCron` /
  interval presets):
  the scan just sweeps for what's due; per-reminder timing lives in the DB (`NextOccurrenceAt`), never as a Quartz trigger.
- **Two misfire/catch-up layers (don't conflate them):** (1) the **Quartz-level misfire policy** on the registration — `ReminderScanOptions.MisfirePolicy`, default `FireAndProceed`: after the scanner
  was down, fire **one** scan on recovery to catch up, then resume (the scan is idempotent + DB-deduped, so a single catch-up fire sweeps everything that came due — no storm); (2) the **per-occurrence
  catch-up** the 03a handler owns — for one reminder with several missed instants it sends only the most-recent and collapses the older ones to
  `Skipped` rows.
- **No clustering; DB-side dedup is the safety net.** This does **not** push Scheduler toward
  `UsePersistentStore` / clustering (single-node modular monolith). `DisallowConcurrent` keeps two scans from overlapping, and the append-only dispatch log's `(ReminderDefinitionId, OccurrenceAt)`
  dedup is the no-double-send guarantee even across a volatile RAM job store or a double-fire. Cluster-safe scheduling is a repo-wide infra decision to flag in **Scheduler**, not bolted on here.

Tests: `…/integration/reminders/RemindersScheduledJobsRegistrarTests.cs` (the built registration has the expected key/handler/cadence/concurrency; registering via the real `IScheduler` persists one
`ScheduledJob` row + one live trigger; reconcile-on-boot is a no-duplicate upsert; cron overrides the interval) +
`…/unit/reminders/RemindersNoQuartzGuardTests.cs` (the module references no Quartz assembly and defines no Quartz `IJob`). No frontend prompt — pure infrastructure.

## Dispatch policy — per-kind opt-out & quiet hours (phase 04a — `domain/entity/` + `…/endpoint/preference/`)

The user-facing preference layer **on top of** the phase-03 scanner. **Strictly additive:** with no preference rows the behaviour is exactly phase 03 (one immediate `NotifyAsync` per due occurrence).
Two new plain-table, domain-free `[NoAudit]` entities (mirroring `NotificationPreference`'s NoAudit choice; plain indexed `UserId`, no FK to the user table) plus three **user-scoped** endpoints. New
migration `RemindersDispatchPolicy` in both portals.

| Entity                   | Key                                  | Role                                                                                                                                                                                         |
|--------------------------|--------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `ReminderKindPreference` | unique `(UserId, OwnerModule, Kind)` | Per-reminder-**kind** opt-out. **Absence-means-enabled** — only an explicit `Enabled = false` row mutes a kind for a user. Carries an **advisory** `ChannelHint` (not enforced — see below). |

> **⚠️ `ReminderQuietHours` is gone (notifications follow-up 05, 2026-07-20).** There is one quiet-hours window
> per user for the whole deployment and the **Notifications** module owns it (`NotificationQuietHours`), because
> the notification dispatcher needs the same window to defer Web Push/Email. The table was migrated across
> (data-carrying migration `NotificationQuietHours` in both portals) and `SetReminderQuietHoursEndpoint` +
> `MyReminderPreferencesResponse.QuietHours` were removed — edit the window at
> `GET|PUT|DELETE /notification-quiet-hours`. **The scan-side behaviour below is unchanged**; only the source of
> the window moved, to the Kernel `IQuietHoursReader` seam (`Core.Reminders/infrastructure/NoQuietHoursReader.cs`
> is the no-op for a host shipping Reminders without Notifications). The window math moved with it, from
> `ReminderQuietHoursPolicy` to `Kernel.notification.QuietHoursPolicy`.

**Scanner integration (`ReminderScanJobHandler`, after recipient resolution):**

- **Per-kind opt-out** filters the resolved recipients (`LoadOptedOutUserIdsAsync` over the candidate ids). All recipients muted ⇒ a `Skipped` / **`OptedOut`** dispatch row, no send; the schedule
  still advances.
- **Quiet hours** *defers* (does **not** drop): if **every** remaining recipient is inside their window at the dispatch instant, the occurrence is deferred — **nothing sent, no dispatch row written,
  `NextOccurrenceAt` left unchanged** — and the next scan re-evaluates it (dispatching on the first sweep after the window ends).

**Decisions baked in (flagged — repo/design wins over the prompt's literal wording):**

- **Quiet hours evaluated at the dispatch instant `now`, not the historical occurrence instant.** "Don't disturb the user *right now*"; an overdue occurrence sent during the day isn't suppressed
  because it first came due at night.
- **Defer leaves `NextOccurrenceAt` pointing at the due occurrence — it is NOT pushed to the window end** (the prompt's suggestion). Pushing it forward would make the shared
  `ReminderOccurrenceCalculator` skip the overdue occurrence entirely (it returns the next *scheduled* instant ≥ `from`). Instead the occurrence is re-evaluated each scan — cheap (a quiet-hours check,
  no send, no row) — and dispatched once the window passes. Append-only log preserved.
- **Quiet hours defers only when it would wake *every* recipient** (all-recipients-in-window). A per-occurrence ledger can't be split per recipient without breaking the one-row-per-occurrence dedup,
  so a *mixed* window (some quiet, some not) dispatches to all. Correct for the dominant single-recipient case; a documented v1 simplification.
- **Drop semantics not implemented:** v1 is defer-only (`SkipReason.QuietHours` stays reserved for a future drop policy).
- **Time zone:** the repo models **no per-user/employee time zone** — only the single deployment zone (`Application:Timezone`, the one `WorkHourCalculatorService` uses). Quiet-hours windows are
  interpreted in it (`Kernel.notification.QuietHoursPolicy.IsWithin`, fed `Helper.GetTimezone`); the handler falls back to UTC if the config is missing/invalid. If a per-user time zone is ever added,
  revisit.

**Reconciliation with the Notification module's `NotificationPreference` (orchestration rule 8) — the boundary:**

- **This module decides *whether / when* a scheduled reminder may fire** — per-reminder- **kind** opt-out (mute one *kind* of scheduled reminder, e.g. "probation-ending", without muting all push) and
  **quiet-hours deferral**. These are scheduling-side concepts the Notification module has none of.
- **The Notification module still owns the final channel transport** + its own per- **channel** `NotificationPreference`
  filtering. We do **not** re-implement channel transport or per-channel enable.
- **`ChannelHint` is advisory only.** `INotificationService.NotifyAsync(recipients, type, payload, ct)` carries **no channel selector** (verified against the contract), so neither the per-definition
  `ChannelHint` nor the per-kind one can be *enforced* from here — they are stored as metadata and the Notification module owns routing. Only the per-kind **opt-out** (suppress the kind) is enforced.
  **Proposal if a customer needs per-kind channel routing:** add a channel-preference overload to the Notification contract (`NotifyAsync` accepting a channel preference) — do **not**
  bolt a channel-selection path onto this module.

**Endpoints** (`application/endpoint/preference/`, route prefix `/reminder-preference`, **user role and up** via
`GetUserRole()` — every read/write keyed by `User.GetId()`, so strictly self-scoped, no cross-user path):

- `GetMyReminderPreferencesEndpoint` (GET `/reminder-preference`) — the caller's kind opt-outs + quiet hours (`MyReminderPreferencesResponse`).
- `UpdateReminderKindPreferenceEndpoint` (PUT `/reminder-preference/kind`) — upsert one `(OwnerModule, Kind)` opt-out (concurrent-insert race fallback like the Notification endpoint).
- ~~`SetReminderQuietHoursEndpoint` (PUT `/reminder-preference/quiet-hours`)~~ — **removed**; the window is set at
  `PUT /notification-quiet-hours` (Notifications) and cleared with `DELETE` on the same route.

Aggregate admin reads are deferred to the dashboard (phase 05); these endpoints are self-scoped only.

Tests: `…/unit/notification/QuietHoursPolicyTests.cs` (pure window math — same-day, overnight wrap, degenerate, plus the `ResumeAt`/DST cases the Notifications side needs; formerly
`ReminderQuietHoursPolicyTests`); scanner policy scenarios in `…/integration/reminders/ReminderScanJobHandlerTests.cs` (opt-out removes a recipient / all-opted-out ⇒ `Skipped`/`OptedOut` /
absence-means-enabled / opt-out scoped by owner+kind / quiet-hours defer then later dispatch / mixed-window dispatches); `…/integration/reminders/ReminderPreferenceEndpointTests.cs` (auth matrix,
upsert/no-duplicate, quiet-hours validation, self-scoping/IDOR). Frontend prompt: `frontend-prompts/04-dispatch-policy.md`.

## Dispatch policy — digest batching (phase 04b — `infrastructure/scheduling/` + `application/job/`)

Opt-in aggregation **on top of** the scanner. **Strictly additive:** a definition with no
`ReminderDefinition.DigestKey` is exactly phase 03 / 04a (one `NotifyAsync` per due occurrence). A definition that carries a `DigestKey` is routed to `ReminderScanJobHandler.ProcessDigestKeyAsync`
instead: the key's due definitions are aggregated so **each recipient gets one `NotificationType.ReminderDigest` notification** across every definition sharing the key they're a recipient of — while
the handler **still writes one per-occurrence `ReminderDispatch` row per definition** (the audit trail is never collapsed; phase-05 snooze/dismiss stays per occurrence).

**The new `ReminderDigest` notification type** was added to `Kernel.notification.NotificationType` with a renderer case in `Core.Notifications/application/NotificationTextRenderer` (the module's
documented "add a notification type"
playbook — mirrors how the existing `UpcomingHrEvents` digest works: aggregation in the caller, rendering in Notifications). The aggregate `NotifyAsync` carries a **count-by-`Kind`** payload (mirrors
`NotifyUpcomingHrEventsJobHandler`;
`Kind` is a non-PII category string). Dispatch still goes only through the Notification contract — no digest-specific transport exists or was added.

**The window is a property of the `DigestKey`, not the row** — a digest aggregates across *all* definitions sharing the key, so a per-definition column can't express it (01b added the `DigestKey`
column but **no** window column). Configured in **`ReminderDigestOptions`** (`infrastructure/scheduling/`, bound from the `ReminderDigest` config section in the HBCleaning `Program.cs` alongside
`ReminderScan`): `Windows[digestKey]` → `ReminderDigestWindow` with a rolling
`WindowMinutes` **or** a `DailyAnchorMinute` (local minutes-from-midnight). A `DigestKey` with **no** entry defaults to **batch-per-run** (flush every scan). The flush decision is pure math in **
`ReminderDigestPolicy.ShouldFlush`**
(`domain/service/`, unit-tested like the shared `QuietHoursPolicy`).

**Aggregation algorithm (`ProcessDigestKeyAsync`, one key per call, each key isolated):**

- Each definition contributes its **catch-up target** (the most-recent due occurrence; older missed instants collapse to `Skipped`/`Other` rows exactly as the per-occurrence path — one send per
  reminder per run, no stale storm). So **N occurrences in a digest = N definitions**, not N occurrences of one definition.
- Recipient resolution + the 04a per-kind opt-out are shared with the per-occurrence path via the extracted
  `ResolveRecipientsAsync` (resolver-missing/throw ⇒ `Failed`, no recipients ⇒ `Skipped`/`NoRecipients`, all opted out ⇒ `Skipped`/`OptedOut`); a definition that resolves to a terminal row never
  enters the digest and terminalises immediately, independent of the flush window.
- **One flush decision per key** (the window is per key) driven by the **earliest due target across the key's definitions**. Not yet elapsed ⇒ **every candidate is left untouched — no rows, no
  advance** — and the next scan reconsiders it (the row-present⇒skip dedup keeps that safe). This is the deliberate v1 simplification: all recipients of a key share one flush timing, mirroring the 04a
  "one ledger row per occurrence" constraint.
- **Quiet hours compose:** if **every** recipient across the batch is in their window at `now`, the whole flush is **deferred** (nothing sent, no rows) so a quiet-hours occurrence is never prematurely
  digested; a mixed window dispatches to all (same rule as 04a).
- On flush: **one `NotifyAsync` per recipient** (grouped, isolated — at-least-one success ⇒ `Sent` rows, all-throw ⇒
  `Failed` rows, schedule advances either way), then one per-definition dispatch row (all sharing the digest correlation id) + the collapsed older rows, then advance every definition.

Tests: `…/unit/reminders/ReminderDigestPolicyTests.cs` (batch-per-run / rolling-window elapsed-or-not / daily-anchor before-after-and-next-day); digest scenarios in
`…/integration/reminders/ReminderScanJobHandlerTests.cs` (N definitions ⇒ one `NotifyAsync` + N per-occurrence rows; window-not-elapsed holds then flushes; opted-out recipient excluded; all recipients
in quiet hours defers then later digests); `…/unit/notification/NotificationTextRendererTests.cs` (the
`ReminderDigest` body). **No frontend prompt** — digest is owner-configured per kind, no user screen.

## Dashboard reads & exports (phase 05a — `application/endpoint/dashboard/` + `…/export/` + `…/service/`)

The read surface over phases 01–04: pure `AsNoTracking` projections (the 01c `Projection`s) + CSV exports — **no new sending logic, no state changes** (snooze/dismiss is 05b). All endpoints share the
route prefix
`/reminder-dashboard/…` (auto-tag `reminder-dashboard`, distinct from the 02 inspector's `reminder-definition`), following the repo's route-prefix-tag convention (no FastEndpoints `Group`/`SubGroup`,
as flagged in 02a). Filtering is shared between each JSON endpoint and its `Export*` twin via `ReminderDashboardQueries`, so the file can never drift from the API.

| Endpoint                                | Route                                              | Role  | Notes                                                                                                                                                                                                                                                                                                                                                                                                                                                |
|-----------------------------------------|----------------------------------------------------|-------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `GetUpcomingRemindersEndpoint`          | POST `/reminder-dashboard/upcoming`                | Admin | Upcoming across all modules — **one row per active definition showing its single soonest `NextOccurrenceAt`**; later occurrences are **not** expanded (recomputation, not SQL-translatable). Base scope `NextOccurrenceAt != null`. Filter: owner/subject/kind/recipient/schedule-type/status/occurrence-range. Default order: soonest first.                                                                                                        |
| `ExportUpcomingRemindersEndpoint`       | POST `/reminder-dashboard/upcoming/export`         | Admin | Same filtered query, whole result set, CSV.                                                                                                                                                                                                                                                                                                                                                                                                          |
| `GetMyUpcomingRemindersEndpoint`        | POST `/reminder-dashboard/my-upcoming`             | User+ | **Self-scoped** to the caller's explicit `ReminderRecipient.UserId` rows. **Resolver-strategy reminders are excluded** — a read path must never invoke an `IReminderRecipientResolver` (resolvers run at dispatch and may reach owner domain code). Projects a **trimmed self-view DTO** (`MyUpcomingReminderDto`) — no `PayloadJson`, no co-recipient list, no resolver/digest/channel metadata; the full `ReminderDefinitionDto` stays admin-only. |
| `GetReminderDispatchHistoryEndpoint`    | POST `/reminder-dashboard/dispatch-history`        | Admin | Straight projection of the append-only ledger, **reversals included**. Filter: reminder/owner/recipient/outcome/dispatched-range. Default order: most recent first.                                                                                                                                                                                                                                                                                  |
| `ExportReminderDispatchHistoryEndpoint` | POST `/reminder-dashboard/dispatch-history/export` | Admin | Same filtered query, whole result set, CSV.                                                                                                                                                                                                                                                                                                                                                                                                          |
| `GetReminderOverviewEndpoint`           | GET `/reminder-dashboard/overview`                 | Admin | Roll-up: upcoming by owner/kind/next-7-days, recent (last-7-day) dispatch outcomes, paused/cancelled counts, and **effective-`Failed`** (not superseded by a later reversal) needing attention.                                                                                                                                                                                                                                                      |

**Decisions baked in (flagged — repo/design wins over the prompt's literal wording):**

- **CSV only, no XLSX.** The 05a prompt says "mirror Attendance's export (CSV/XLSX)" *and* references EmployeeModule's
  `SimpleCsv`. Attendance's XLSX is Syncfusion (a licensed dependency) on a payroll-accountant handoff; these dashboard exports are an ops/audit dump, so a **dependency-free CSV** (UTF-8 BOM,
  RFC-4180, ISO-8601 instants, ids only — no PII) keeps this domain-free infra module's reference graph to just Kernel + Framework. The
  `ReminderExportFormat` enum + `ReminderExportFormatParser` keep the Attendance-style `?format=` endpoint shape so an XLSX variant can slot in later; an unsupported format ⇒ 400. Impl:
  `application/service/ReminderExportService`
  (`IReminderExportService` in `domain/serviceContract/`, `ISingletonService`).
- **No FastEndpoints grid base.** These don't extend `BaseGridEndpoint` (it derives the route from the entity name — two grids over `ReminderDefinition` would collide, and there's no domain-free
  prefix hook). They mirror Inventory's
  `ExpiringStockReportEndpoint`: a plain `Endpoint<BaseFilterSortPaginateRequest<TFilter>, BaseGridResponse<TDto>>`
  with an explicit route, calling `GetGridDataAsync` directly. A default `SortBy` is substituted when the request omits one.
- **Dispatch-history recipient filter is by the definition's *current* explicit recipients, not the row's snapshot.**
  The true recipients are the immutable `RecipientsSnapshot` jsonb; filtering that isn't cleanly SQL-translatable, so
  `RecipientUserId` filters `ReminderDefinition.Recipients` (documented on the filter DTO). The snapshot is still returned verbatim per row.
- **The dispatch log is the source of truth.** The history view is a straight projection of the ledger; where an overview count and the log disagree, the log wins. No read recomputes scheduler state.

**Boundary vs the Scheduler module:** this dashboard is **per-reminder** (upcoming occurrences, per-occurrence dispatch history). The **job-level** run view (did `reminders.scan` fire, last/next run,
outcomes) belongs to the **Scheduler module's** run-log dashboard — not duplicated here.

Tests (`…/integration/reminders/`): `GetUpcomingRemindersEndpointTests` + `GetReminderDispatchHistoryEndpointTests`
(grid auth matrix via `BaseGridEndpointTests` + per-column filters, default order, base-scope/reversal fidelity),
`GetMyUpcomingRemindersEndpointTests` (auth, self-scoping/IDOR, resolver-strategy exclusion),
`GetReminderOverviewEndpointTests` (counts reconcile with definitions + log incl. the effective-failed rule),
`ReminderDashboardExportTests` (CSV headers + row fidelity, format default + unsupported-format 400, auth matrix). **The dashboard frontend prompt is written in 05b**, once snooze/dismiss exists.

## Per-recipient snooze / dismiss (phase 05b — `domain/entity/ReminderOccurrenceAction.cs` + the scanner + two endpoints)

The recipient self-service layer that lets a user defer (snooze) or suppress (dismiss) **their own** delivery of a single upcoming occurrence — wired into the scanner at the **same resolve-recipients
seam** the 04a per-kind opt-out used, just a later phase. **Strictly additive:** with no action rows the scanner behaves exactly as 03/04. New migration `RemindersSnoozeDismiss` in both portals.

**The per-recipient model (the whole point — pin this).** A reminder can have many recipients but only one **shared** `ReminderDefinition.NextOccurrenceAt`. So snooze/dismiss state is recorded **per
`(ReminderDefinition, OccurrenceAt, UserId)`** in the append-only `ReminderOccurrenceAction` ledger — **never**
by mutating the shared `NextOccurrenceAt` or any past `ReminderDispatch` row. One recipient acting never moves the schedule for the others.

- **Dismiss** — suppresses the caller's delivery of one occurrence. Reversible only by a reversal row.
- **Snooze** — defers the caller's delivery of one occurrence to `SnoozeUntil`. The snoozed
  `(occurrence, recipient, SnoozeUntil)` becomes its own due item, re-evaluated by a **second due-source**.

**Scanner integration (`ReminderScanJobHandler`):**

- **(a) Resolve-recipients drop** (`ApplyOccurrenceMarkersAsync`, inside the shared `ResolveRecipientsAsync`, so the per-occurrence **and** digest paths both honour it — rule 7 keeps the unit of
  interaction the occurrence). A recipient with an effective (latest, non-reversed) snooze/dismiss action for the due occurrence is dropped, with a **per-recipient** `Skipped` row
  (`SkipReason.Dismissed` / `Snoozed`,
  `RecipientsSnapshot = [thatUserId]`) for audit. The other recipients still receive the occurrence and the shared schedule advances normally; if *every* recipient was dropped, the occurrence
  terminalises (rows already written) and advances — nothing sent.
- **(b) Second due-source** (`ProcessDueSnoozesAsync`, after the per-occurrence + digest loops). The definition scan can never revisit a snoozed occurrence — `NextOccurrenceAt` has already advanced
  past it — so due snoozes are dispatched here: query snooze markers with `SnoozeUntil <= now`, **not reversed**, **not superseded** by a later action for the same key, and **not delivered**, then
  send each as a **single-recipient** notification (reusing 03a's text precedence) and append a recipient-scoped, **marker-linked**
  dispatch row. `NextOccurrenceAt` is **untouched**.

**Dispatch-policy interaction on the re-delivery path (04a).** The second due-source applies the **per-kind opt-out** but **not** quiet hours: a recipient who muted the kind (`Enabled = false`) after
snoozing has the re-delivery dropped to a marker-linked `Skipped`/`OptedOut` row (the snooze still resolves and never re-fires —
"mute this whole kind" outranks a stale snooze). Quiet hours deliberately do **not** re-defer a due snooze:
the user explicitly chose the `SnoozeUntil` instant, so it is honoured even inside a quiet window (unlike the per-occurrence path, where the user chose no instant and quiet hours defer).

**Marker-state dedup (pin this — not the occurrence-keyed dedup).** The original occurrence usually already carries a `Sent` dispatch row (the other recipients got it), so the 03a
`(definition, occurrence)` dispatch dedup **cannot** gate the snoozed re-delivery — it would wrongly skip the snoozer. Instead a snooze is considered **delivered** once a `ReminderDispatch` references
it via `ReminderDispatch.ReminderOccurrenceActionId`. The due-snoozes query excludes delivered markers, so a snooze fires **once** and never re-fires. A failed re-delivery still writes a
(non-reversed) marker-linked `Failed` row, so it isn't retried (a re-dispatch would be a reversal) — mirroring the per-occurrence path.

**Endpoints** (`application/endpoint/dashboard/`, route prefix `/reminder-dashboard/…` (auto-tag), **User role and up**, append-only):

- `SnoozeReminderOccurrenceEndpoint` (POST `/reminder-dashboard/snooze`) — appends a `Snooze` action; validates
  `SnoozeUntil` is in the future and `OccurrenceAt` is the definition's pending occurrence.
- `DismissReminderOccurrenceEndpoint` (POST `/reminder-dashboard/dismiss`) — appends a `Dismiss` action; validates `OccurrenceAt` is the definition's pending occurrence.
- **Occurrence validation:** both require `OccurrenceAt == def.NextOccurrenceAt` (the only occurrence instant the dashboard surfaces to a recipient) — a recipient cannot fabricate an
  `(OccurrenceAt, SnoozeUntil)` that was never scheduled and make the engine re-deliver it. Mismatch → 400.
- **Self-scoping / IDOR:** both gate on `ReminderOccurrenceActionGuard.ResolveRecipientOccurrenceAsync` (which resolves recipiency + the pending occurrence in one query) — a user may act **only** on a
  definition they are an *explicit* recipient of. A missing definition, a **resolver-strategy**
  definition (recipients unknown until dispatch; a request path must never invoke a resolver), or a non-recipient all return a **uniform 404** (never leaks existence). Append-only: a second action is
  a new row, never an update.

Tests: `…/integration/reminders/ReminderScanJobHandlerTests.cs` (snooze defers for the snoozing recipient only — others + `NextOccurrenceAt` untouched — then fires at the new time; the re-delivery is
**not** suppressed by the original `Sent` row and does not re-fire once resolved; dismiss suppresses for the caller only and is append-only; a snooze superseded by a later dismiss never fires) +
`ReminderOccurrenceActionEndpointTests.cs` (auth matrix, append-only persistence, future-instant + occurrence-match validation (a fabricated future occurrence ≠
`NextOccurrenceAt` → 400, writes nothing), self-scoping/IDOR incl. resolver-strategy + unknown-definition 404). Frontend prompt: `prompts/reminders/frontend-prompts/05-dashboard-and-queries.md`.

## Ledger retention (follow-up 01 — `application/job/PurgeExpiredReminderLedgersJobHandler.cs`)

Both ledgers are append-only, so without a purge they grow for the life of the deployment — GDPR Art. 5 (1)(e) / §13 zák. 18/2018 (audit L1). The module dogfoods its own substrate: a **second**
recurring job (`Reminders.PurgeExpiredDispatchLog`) alongside the scan, registered through the same Kernel
`IScheduler` contract, monthly at `0 45 3 1 * ?`. Still no Quartz reference.

**The ordering is the point.** Every FK into these tables is `Restrict` (history is never cascade-erased with its definition), so a naive delete aborts the whole batch. Three passes, each excluding
rows still referenced:

1. `reminder_dispatch` — first, since it is the only table pointing at the other two. Skips rows another dispatch still reverses (`ReversesDispatchId`).
2. `reminder_occurrence_action` — second, so a marker whose fulfilling dispatch just went in pass 1 is collectable in the *same* run. Skips rows referenced by `ReversesActionId` or by a surviving
   `ReminderDispatch.ReminderOccurrenceActionId`.
3. `reminder_definition` — last, `Cancelled`/`Completed` only, and only once both ledgers are clear.
   `Active`/`Paused` are never eligible at any age. **Tracked, not set-based**, because the definition is an audited entity and the deletion should reach `audit_log` (`PayloadJson` is
   `[AuditIgnore]`).

Passes 1–2 are each one `ExecuteDeleteAsync` written as plain LINQ: age gate → keep-last-N floor (`Count(newer => …) >= keepLastN`) → that pass's FK guards. There is deliberately **no shared delete
helper** — what's shared with the Scheduler run-log purge is the *policy shape*
(`Sydowwe.Framework/…/retention/RetentionOptions.cs`), not the query, because the FK guards are the part that differs per ledger and can't be abstracted. `ExecuteDeleteAsync` is correct here — both
ledgers are
`[NoAudit]`, and a retention purge must not write audit rows describing what it just erased.

`ReminderRetentionOptions` (section `ReminderRetention`, bound in the HBCleaning `Program.cs`): default 3 years / keep last 20 per definition, plus `Enabled` (freeze everything) and
`PurgeTerminalDefinitions`
(freeze pass 3 only). Windows are operational policy, **not** statutory — deliberately not in
`docs/routineLawCheckups.md`. Tests:
`…/integration/reminders/PurgeExpiredReminderLedgersJobHandlerTests.cs` (15).

> **Not covered:** per-user erasure. This purges on *age*, so it cannot reach an anonymized user's rows
> on demand (audit L2) — that is the separate on-demand axis, see "Per-subject erasure" below.

## Per-subject erasure (follow-up 03 — audit L2, closed 2026-07-20)

`application/service/ReminderSubjectDataEraser.cs` implements the Kernel fan-out
`MojaDigitalnaFirma.Kernel/gdpr/ISubjectDataEraser.cs`, which `EmployeeErasureService` composes as
`IEnumerable<ISubjectDataEraser>` — the write-side twin of the `IEmployeePersonalDataProvider`
composition, so `EmployeeModule` still references nothing here. `SubjectErasureRequest` carries **both**
`EmployeeId` and `UserId` (this module keys everything by `UserId`; the bridge is resolved once at the call site). Unlike follow-up 02's provider, this one lives **in the module** — the contract is a
Kernel type, so no domain reference is needed. Mutates the ambient `DbContext` and never commits; exceptions are allowed to escape (a silently skipped erasure is an Art. 17 hole, and the caller rolls
back).

| Table                                          | Action                                                                                                                                                                                          |
|------------------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `ReminderRecipient`                            | delete (tracked — audited entity)                                                                                                                                                               |
| `ReminderKindPreference`                       | delete                                                                                                                                                                                          |
| `ReminderDefinition` left with zero recipients | → `Cancelled`, `IsActive = false`, `NextOccurrenceAt = null` (else the scan appends a `Skipped`/`NoRecipients` row forever; `Cancelled` keeps the row re-registerable)                          |
| `ReminderOccurrenceAction.UserId`              | **pseudonymize** → `0` — append-only evidence, referenced by dispatches through a `Restrict` FK; its `(DefinitionId, OccurrenceAt, UserId)` index is non-unique, so the shared sentinel is safe |
| `ReminderDispatch.RecipientsSnapshot`          | scrub the id out of the jsonb array (candidates narrowed in SQL with `@>` / `EF.Functions.JsonContains`)                                                                                        |

**Recipient-side only.** Rows *addressed to* the subject go; rows *about* them belonging to someone else stay — subject-side payload PII degrades through the render-time
`INotificationPayloadEnricher`, by design, and is deliberately not scrubbed here. `ReminderDefinition.PayloadJson` / `.SubjectId` are therefore untouched and recorded as a **known residual** (opaque,
owner-supplied; tightening it belongs to
`prompts/payload-pii-contract.md`). Tests:
`…/integration/service/gdpr/SubjectDataErasureTests.cs` (7, incl. the scope guard).

## GDPR cataloguing (follow-up 02 — audit L3, closed 2026-07-20)

Reminder data is now visible to both GDPR surfaces, and **neither addition lives in this module** — its reference set is still Kernel + Framework only.

- **RoPA (Art. 30):** `ProcessingActivity` `reminder-dispatch` / `ZSC-12`, seeded by the GDPR module's
  `DevProcessingActivitySeeder`. RoPA is dev-seed-only + admin CRUD in vanilla, so that row is the default template a customer's DPO edits. Legitimate interest, `DataSubjectCategory.Employee`, no
  special category, retention pointing at the 3-year `ReminderRetention` window above. Category-only — it names no module and no data subject.
- **DSAR (Art. 15\20):** `AdhdTimeOrganizer/infrastructure/service/ReminderPersonalDataProvider.cs`
  contributes a `"reminders"` section to `EmployeeDataExportService` through the existing
  `IEmployeePersonalDataProvider` seam. It sits in the **composition project**, not here: that interface is an `EmployeeModule.Contracts` type and bridging `employeeId → UserId` needs the `Employee`
  entity itself. Slice = explicit `ReminderRecipient` rows + the subject's own `ReminderOccurrenceAction`
  history + their `ReminderKindPreference` rows. `RecipientMode == ExplicitUsers` only — a read path never invokes an `IReminderRecipientResolver` (same rule as `GetMyUpcomingRemindersEndpoint`);
  `PayloadJson` and co-recipient ids are excluded. Tests:
  `…/integration/service/reminders/ReminderPersonalDataProviderTests.cs`.
- Quiet hours are **not** exported here — the window belongs to Notifications (`NotificationQuietHours`) since notifications follow-up 05, and to that module's own L3.

## Deeper reference

- Ledger retention: `Core.Reminders/application/job/PurgeExpiredReminderLedgersJobHandler.cs` +
  `ReminderRetentionOptions.cs`; registered via `RemindersScheduledJobsRegistrar.RetentionPurgeRegistration`; shared policy shape in
  `Sydowwe.Framework/infrastructure/persistence/retention/RetentionOptions.cs`. Tests:
  `…/integration/reminders/PurgeExpiredReminderLedgersJobHandlerTests.cs`.
- Build plan: `prompts/reminders/` (orchestration in `00-orchestration.md`).
- Dashboard reads/exports: `Core.Reminders/application/endpoint/dashboard/` (`GetUpcomingRemindersEndpoint`,
  `GetMyUpcomingRemindersEndpoint`, `GetReminderDispatchHistoryEndpoint`, `GetReminderOverviewEndpoint`, the two
  `Export*` endpoints, `ReminderDashboardQueries`) + `…/application/dto/dashboard/` +
  `…/application/export/` + `…/application/service/ReminderExportService.cs` +
  `…/domain/serviceContract/IReminderExportService.cs`. Tests: `…/integration/reminders/Get*EndpointTests.cs` +
  `ReminderDashboardExportTests.cs`.
- Snooze / dismiss: `Core.Reminders/domain/entity/ReminderOccurrenceAction.cs` + `…/domain/enum/ReminderActionType.cs`
    + `…/infrastructure/persistence/configuration/ReminderOccurrenceActionEntityConfiguration.cs`; the
      `ReminderDispatch.ReminderOccurrenceActionId` link; scanner members `ApplyOccurrenceMarkersAsync` /
      `LoadEffectiveActionsAsync` / `ProcessDueSnoozesAsync` / `DispatchSnoozedOccurrenceAsync` in
      `…/application/job/ReminderScanJobHandler.cs`; endpoints `…/application/endpoint/dashboard/SnoozeReminderOccurrenceEndpoint.cs`
    + `DismissReminderOccurrenceEndpoint.cs` + `ReminderOccurrenceActionGuard.cs` + `…/application/dto/dashboard/Snooze|DismissReminderOccurrenceRequest.cs`; migration `RemindersSnoozeDismiss` in both
      portals. Tests: `…/integration/reminders/ReminderOccurrenceActionEndpointTests.cs`
    + the snooze/dismiss scenarios in `ReminderScanJobHandlerTests.cs`. Frontend prompt:
      `prompts/reminders/frontend-prompts/05-dashboard-and-queries.md`.
- Dispatch policy: `Core.Reminders/domain/entity/ReminderKindPreference.cs` + `…/application/endpoint/preference/` +
  `…/application/dto/preference/`; migration `RemindersDispatchPolicy` in both portals. Quiet hours moved out — entity `Core.Notifications/domain/entity/NotificationQuietHours.cs`, math
  `Kernel/notification/QuietHoursPolicy.cs`, seam `Kernel/notification/IQuietHoursReader.cs` (impl `Core.Notifications/infrastructure/QuietHoursReader.cs`, fallback
  `Core.Reminders/infrastructure/NoQuietHoursReader.cs`), migration `NotificationQuietHours` in both portals. Tests: `…/unit/notification/QuietHoursPolicyTests.cs` +
  `…/integration/reminders/ReminderPreferenceEndpointTests.cs`
  (kind opt-outs) + `AdminPortal.Tests/…/notification/NotificationQuietHoursEndpointTests.cs` (the window).
- Digest batching: `Core.Reminders/infrastructure/scheduling/ReminderDigestOptions.cs` (`ReminderDigestWindow`) +
  `…/domain/service/ReminderDigestPolicy.cs` + the `ProcessDigestKeyAsync`/`SendDigestsAsync`/`BuildDigestPayload`
  members of `…/application/job/ReminderScanJobHandler.cs`; `NotificationType.ReminderDigest` + the renderer case in
  `Core.Notifications/application/NotificationTextRenderer.cs`; bound in the HBCleaning `Program.cs`. Tests:
  `…/unit/reminders/ReminderDigestPolicyTests.cs` + the digest scenarios in `…/integration/reminders/ReminderScanJobHandlerTests.cs`.
- Scan registration: `Core.Reminders/infrastructure/scheduling/RemindersScheduledJobsRegistrar.cs` +
  `ReminderScanOptions.cs`; wired in `HbCleaningServiceExtensions.AddHbCleaning` + bound in the HBCleaning `Program.cs`.
- Registry + endpoints: `Core.Reminders/infrastructure/ReminderRegistryService.cs` +
  `…/application/endpoint/reminderDefinition/command/` + `…/application/dto/reminderDefinition/Register…`,`ReminderKeyRequest`.
- Inspector reads: `…/application/endpoint/reminderDefinition/query/` (`GetReminderByIdEndpoint`,
  `ReminderDefinitionGridEndpoint`) + `…/application/dto/reminderDefinition/ReminderDefinitionFilterRequest.cs`.
- Inspector tests: `…/integration/reminders/GetReminderByIdEndpointTests.cs` + `ReminderDefinitionGridEndpointTests.cs`.
- Frontend prompt: `prompts/reminders/frontend-prompts/02-registration-api.md`.
- Scan handler: `Core.Reminders/application/job/ReminderScanJobHandler.cs` (`reminders.scan`) +
  `…/integration/reminders/ReminderScanJobHandlerTests.cs`.
- Next-occurrence helper: `Core.Reminders/domain/service/ReminderOccurrenceCalculator.cs` (shared with phase 03).
- Cron evaluator: `MojaDigitalnaFirma.Kernel/scheduling/ICronEvaluator.cs` + `Core.Scheduler/infrastructure/CronEvaluator.cs`.
- Registry/endpoint tests: `MojaDigitalnaFirma.HBCleaning.Tests/integration/reminders/ReminderRegistryServiceTests.cs`
    + `ReminderCommandEndpointTests.cs` (+ `infrastructure/FakeReminderStrategies.cs`).
- Contract source: `MojaDigitalnaFirma.Kernel/reminders/`.
- Persistence: `Core.Reminders/domain/entity/` + `…/infrastructure/persistence/configuration/`.
- Guard test: `MojaDigitalnaFirma.HBCleaning.Tests/unit/reminders/ReminderContractGuardTests.cs`.
- Read DTOs: `Core.Reminders/application/dto/reminderDefinition/` + `…/reminderDispatch/`.
- Persistence tests: `MojaDigitalnaFirma.HBCleaning.Tests/integration/reminders/ReminderPersistenceTests.cs`
  (+ `infrastructure/ReminderSeedHelper.cs`).
- DTO projection tests: `MojaDigitalnaFirma.HBCleaning.Tests/integration/reminders/ReminderDtoProjectionTests.cs`.
