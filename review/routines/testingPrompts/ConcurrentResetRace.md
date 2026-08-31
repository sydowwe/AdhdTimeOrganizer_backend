# TEST-7 — Two reset sites racing on the completion row (pins `SEC-3`)

Read `_SHARED-CONTEXT.md` first.

## The bug being pinned

Three paths apply a lazy reset and insert a `RoutinePeriodCompletion`:

- `application/endpoint/routineTodoList/query/GetAllGroupedRoutineTodoListEndpoint.cs:63-67` (a **GET**
  that writes)
- `application/endpoint/routineTodoList/command/RoutineToggleIsDoneTodoListEndpoint.cs:45`
- `infrastructure/jobs/RoutineTodoListResetJobHandler.cs:67-68`

The row is uniquely keyed on `(TimePeriodId, PeriodStart)`
(`infrastructure/persistence/configuration/RoutinePeriodCompletionConfiguration.cs:31`). None of the
three takes a lock, re-reads, or handles a unique violation.

Two browser tabs hitting the grouped read in the same second — or a grouped read landing while the
02:00 sweep is in flight — both compute the same elapsed period and both insert. The loser gets an
unhandled `DbUpdateException`. On the job that is the worse case: the insert is a **single**
`SaveChangesAsync` over every period's completions, so one collision means no later period in that
batch resets that night. `DisallowConcurrent = true` guards job-vs-job only, not job-vs-request.

## What to write

New file `Routines/RoutineResetConcurrencyTests.cs`. Concurrency tests are worth writing carefully or
not at all — a flaky one is worse than none. Two approaches, in preference order:

### A. Deterministic — force the collision without threads (recommended)

Do not race anything. Seed an elapsed period, then **insert the completion row that the reset is about
to produce** directly through `CreateDbContext()`, computing its `PeriodStart` the same way
`RoutineResetService.TryReset` does (`:235-247`: `periodEnd = DateOnly.FromDateTime(nextReset)`,
`PeriodStart = periodEnd.AddDays(-LengthInDays)`). Then call the endpoint / run the job and assert it
**succeeds** rather than throwing, and that exactly one completion row exists.

That is the same state a lost race produces, reached deterministically, and it pins the fix
(catch `PostgresErrorCodes.UniqueViolation` and treat it as "someone else already closed this period"
— the mutation is idempotent by construction, so swallowing is correct).

- `GroupedRead_WhenTheCompletionRowAlreadyExists_StillSucceeds`
- `ResetSweep_WhenOnePeriodsCompletionAlreadyExists_StillResetsTheOtherPeriods` — this is the one that
  matters most. Seed **two** elapsed periods (distinct `LengthInDays`!), pre-insert the completion row
  for the first only, run the job, and assert the **second** period reset. Today the batched
  `SaveChangesAsync` fails and the second period is untouched — a silent nightly outage for every
  period after the collision.

### B. Genuinely concurrent — only as a supplement

`GroupedRead_FiredTwiceConcurrently_ProducesExactlyOneCompletionRow`: seed one elapsed period, fire
two `client.GetAsync(...)` calls with `Task.WhenAll`, assert both succeed and exactly one row exists.
Use two separate `HttpClient`s so they do not share a connection. Accept that this may not collide on
every run — do **not** add retries or `Thread.Sleep` to make it collide; keep A as the real pin and let
B be opportunistic. If it proves flaky in CI, delete it and keep A.

## Related, one line, same file

`UpdateRoutineSettings_ConcurrentFirstWrite_DoesNotThrow` — the same shape at
`application/endpoint/routineSettings/UpdateRoutineSettingsEndpoint.cs:36-43`: two first-time `PUT`s
both find no row and both insert against a unique `user_id`. Use approach A (pre-insert the settings
row, then `PUT`) and assert a 204 and a single row. Low severity, one-line fix, trivial test.

## Expected state

All the approach-A tests fail today. If the fix is deferred, tag `[Trait("Status", "KnownGap")]`
naming `SEC-3`.
