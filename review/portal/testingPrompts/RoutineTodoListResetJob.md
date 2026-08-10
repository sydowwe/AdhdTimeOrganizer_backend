# TEST-12 — Integration tests for `RoutineTodoListResetJob`

## Context you need (do not rediscover)

**Repo:** `C:/Users/jakub/RiderProjects/AdhdTimeOrganizer`
**Under test:** `AdhdTimeOrganizer/infrastructure/jobs/RoutineTodoListResetJob.cs` — a Quartz
`IJob` with `[DisallowConcurrentExecution]`, scheduled 02:00 daily in `Program.cs`.
**Collaborator:** `AdhdTimeOrganizer/domain/service/RoutineResetService.cs` — pure static domain
logic (`ComputeNextReset`, `CheckGrace`, `TryReset`), already unit-tested in
`AdhdTimeOrganizer.IntegrationTests/Services/RoutineResetServiceTests.cs`. **Do not duplicate that
file's coverage** — the gaps below are in the *job*, specifically its query shape and its save
gating, which a pure-service test cannot reach.

**Test project:** `AdhdTimeOrganizer.IntegrationTests`. Read
`Infrastructure/AppDbContextFixture.cs` and `Infrastructure/AuthTestBase.cs` first.
Conventions: xunit v3, FluentAssertions, `[Collection("Postgres")]`, real `Program` against a
`Testcontainers.PostgreSql` container. Get a DbContext with `CreateDbContext()`; seed by overriding
`SeedAsync(db)`. `Routines/RoutineNotificationTests.cs` is the closest existing example — copy its
setup shape. Put the new file at `AdhdTimeOrganizer.IntegrationTests/Routines/RoutineTodoListResetJobTests.cs`.

Because this is a Quartz job, resolve it from the test host's service provider and call
`Execute(context)` directly with a stub/mock `IJobExecutionContext` rather than waiting on the
scheduler. Check whether the job takes its dependencies via constructor injection or
`IServiceScopeFactory`, and mirror that.

## Domain model (from `docs/domain-map.md`)

- `RoutineTimePeriod` — a repeating window per user. Fields that matter:
  `LengthInDays` (1–365), `LastResetAt`, `ResetAnchorDay`, `StreakThreshold` (percent, 1–100),
  `Streak`, `BestStreak`, `StreakGraceDays` (0..`LengthInDays-1`), `StreakGraceUntil`,
  `EndingSoonNotifiedFor`, `GraceNotifiedFor`, `IsHidden`.
- `RoutineTodoList` — items inside a period; has `IsDone`, `DoneCount`, `TotalCount`, and a
  `Steps` collection (`TodoListStep`, owned JSON, `Guid` id, each with `IsDone`).
- `RoutinePeriodCompletion` — one history row per (period, period start), carrying `StreakOutcome`
  (`Extended` / `OnGrace` / `Broken` / `NotEvaluated`).

Documented reset rule: completion percentage ≥ `StreakThreshold` → streak++ and `BestStreak` if
higher, grace cleared, outcome `Extended`. Below with `StreakGraceDays > 0` → `StreakGraceUntil =
nextReset + graceDays`, outcome `OnGrace`. Below with no grace → streak zeroed, outcome `Broken`.
An **empty** period is `NotEvaluated` and leaves the streak alone. Then all items **and their steps**
are unticked, a completion row is written, and the summary notification is sent **after** the commit.

## Scenarios to write

### A. `CQ-3` — steps must be unticked at reset (this test should FAIL today)

The job queries `.Include(tp => tp.RoutineTodoListColl)` with **no** `.ThenInclude(t => t.Steps)`.
Lazy-loading proxies are not configured anywhere in the solution, so `Steps` is always empty in the
job and `RoutineResetService.TryReset`'s step-reset loop is a silent no-op.

1. Seed a period due for reset, holding one `RoutineTodoList` item with `IsDone = true` and 3 steps
   all `IsDone = true`.
2. Execute the job.
3. Assert the item is `IsDone == false` **and** all three steps are `IsDone == false`.

Re-read the steps from a **fresh** `CreateDbContext()`, not the seeding context — a tracked in-memory
graph will mask the bug.

### B. `CQ-2` — grace expiry must persist even when no period is due for reset (should FAIL today)

`CheckGrace` mutates `Streak`/`StreakGraceUntil` in memory, but the job returns before
`SaveChangesAsync` whenever `reset.Count == 0`.

1. Seed a period **not** due for reset (`LastResetAt` recent enough that `ComputeNextReset` is in the
   future) whose `StreakGraceUntil` is in the past and whose `Streak > 0`.
2. Ensure no other period in the fixture is due for reset this run.
3. Execute the job.
4. Assert from a fresh context that `Streak == 0` and `StreakGraceUntil` is cleared.

Also write the mirror case: same setup **plus** a second period that *is* due for reset — the grace
break should persist there too (it does today, which is what makes the bug intermittent and worth
pinning from both sides).

### C. Idempotency / double-fire

`Program.cs` currently registers a TEMP `StartNow()` trigger that fires this job on **every boot** in
addition to the 02:00 cron (`CQ-33`), so double-firing is a real production path.

1. Seed a period due for reset.
2. Execute the job **twice** in a row.
3. Assert exactly **one** `RoutinePeriodCompletion` row exists for that period+period-start, streak
   advanced exactly once, and `LastResetAt` advanced exactly one cycle.

If a unique index on `(TimePeriodId, PeriodStart)` is absent (see `MIG-7`), this test will surface it
as duplicate rows rather than a DB error — assert on the row count, not on an exception.

### D. Streak outcomes

One test per branch, each asserting both the period's `Streak`/`StreakGraceUntil` **and** the written
`RoutinePeriodCompletion.StreakOutcome`:

- All items done, ≥ threshold → `Extended`, streak++, `BestStreak` updated when it exceeds the old one.
- Below threshold, `StreakGraceDays > 0` → `OnGrace`, `StreakGraceUntil == nextReset + graceDays`,
  streak unchanged.
- Below threshold, `StreakGraceDays == 0` → `Broken`, streak zeroed.
- **Empty period (no items)** → `NotEvaluated`, streak untouched. This is the easiest branch to get
  wrong by treating 0/0 as 0%.

### E. Notification ordering

The summary notification must be sent **after** the commit, so a notifier failure cannot roll back
the reset. Register a notifier stub that throws, execute the job, and assert the reset and the
completion row are still persisted.

### F. Scope

The job deliberately runs unauthenticated and sweeps **all** users' periods — the global
`IEntityWithUser` filter degenerates to `!IsAuthenticated || …` and lets everything through. Seed
periods for two different users and assert both are reset. This pins intended behavior; do not
"fix" it to be user-scoped.

## Conventions

- AAA, one behavior per test, no shared mutable state between tests.
- Assert from a fresh `CreateDbContext()` for anything the job wrote.
- Scenarios **A** and **B** are expected to fail against current `main`. If you are writing tests
  before the fix lands, tag them `[Trait("Status","KnownGap")]` and reference the finding id
  (`CQ-3`, `CQ-2`) in the trait or an XML comment, so the delta-aware review pass does not re-flag
  them. Remove the trait when the fix ships.
- Do not log or assert on user emails/names.
