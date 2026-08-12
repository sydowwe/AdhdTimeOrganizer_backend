# AdhdTimeOrganizer.Routines — Agent Summary

**Purpose:** the routine domain — recurring to-do items (`RoutineTodoList`) grouped into time
periods (`RoutineTimePeriod`) with streaks, grace windows, budgeted streak freezes and a completion
history (`RoutinePeriodCompletion`); the reset/nudge domain logic (`RoutineResetService`) and the
freeze logic (`RoutineStreakFreezeService`); the two nightly
scheduled jobs that sweep them (keyed `IScheduledJobHandler`s on the Scheduler
module's substrate — this slice references no Quartz); and the notification producer that turns a reset, a lead-time nudge, or
a grace warning into a `Sydowwe.Framework.Contracts` notification.

**This is the third slice of the portal split.** Read `review/portal/slicePrompts/00-README.md` for
the plan and remaining order (History → Planning → Reminders → Tracking — History already landed
before this one, via the membership-source seam below).

## Bounded context

Owns: `RoutineTodoList`, `RoutineTimePeriod`, `RoutinePeriodCompletion`, their EF configurations, the
21 routine endpoints (time-period CRUD + select-options + completion-history + streak-freeze, and the
to-do list
CRUD + grouped-by-period + steps + toggle/reorder), their DTOs and validators, `RoutineResetService`,
`RoutineStreakFreezeService`,
`IRoutinePeriodNotificationService` / `RoutinePeriodNotificationService`,
`RoutineTodoListActivityMembershipSource`, and the `RoutineTimePeriodSeeder` /
`RoutineTodoListSeeder` pair.

Does **not** own — and must never reference: `AppDbContext`, `Program.cs`, the migrations, the DI
wiring, or the host's `PlannerTask`. The completion fan-out into Planning goes through Core's
`RoutineTodoListIsDoneChangedEvent`, consumed by the host's
`PlannerTaskIsDoneChangedEventHandler` — that handler and its sibling
`RoutineTodoListIsDoneChangedEventHandler` stay host-side (see
`review/portal/slicePrompts/00-README.md`'s deferred-decisions list), so this project has no
knowledge of Planning at all.

## Dependency seams

- **References:** `AdhdTimeOrganizer.Core`, `AdhdTimeOrganizer.TodoLists`, `Sydowwe.Framework`,
  `Sydowwe.Framework.Contracts`. No host reference, by construction — see the comment in the csproj.
- **Referenced by:** `AdhdTimeOrganizer` (the host), for the hosted boot registrar
  (`AddHostedService<RoutinesScheduledJobsRegistrar>()`, which pushes the two job schedules to the
  Scheduler module) and the two heartbeat/event-handler call sites that still touch a routine item directly
  (`DesktopActivityHeartbeatEndpoint`, `PlannerTaskIsDoneChangedEventHandler`).
- **`Routines → TodoLists` is the one real outbound slice edge**, verified one-way: `RoutineTodoList`
  derives from TodoLists' `BaseTodoListItem`, and the routine endpoints subclass TodoLists' shared
  toggle/step/reorder endpoint bases. That is structural (inheritance / shared base types), not a
  membership-filter query, so it is **not** a candidate for the seam-inversion trick described below
  — there is no `IQueryable` to hand back through an interface for "this type derives from that
  base." Nothing points back from TodoLists.
- **`RoutineTodoListActivityMembershipSource`** publishes "this activity is on a routine to-do list"
  through Core's `IActivityMembershipSource` seam, so History's grid can filter on routine membership
  without either project referencing the other. Resolution is by string key
  (`ActivityMembershipSourceKeys.RoutineTodoList`) — a missing or misregistered implementation is
  silent, so any consumer needs a behavioural test asserting the rows, not just a route smoke test.

## Gotchas — things that will bite you

- **Everything here takes a plain `DbContext`, never `AppDbContext`.**
  `ModuleServiceExtensions.AddModuleServices` aliases `DbContext` → `AppDbContext` at runtime (global
  query filters and all). At the call site that means no `dbContext.RoutineTimePeriods` — use
  `dbContext.Set<RoutineTimePeriod>()`. The three typed `AppDbContext` DbSet properties
  (`RoutineTodoLists`, `RoutineTimePeriods`, `RoutinePeriodCompletions`) still exist on the host
  context and are used by host-side callers (`DesktopActivityHeartbeatEndpoint`,
  `GetUserDataExportEndpoint`, `PlannerTaskIsDoneChangedEventHandler`) — those are fine, because the
  host is allowed to reference this slice.

- **`Microsoft.EntityFrameworkCore` is a global using** (declared in the csproj) — nearly every file
  here names `DbContext`. Don't be surprised by files with no EF using line.

- **`PeriodCompletionRecord` moved with the endpoints that use it.** It was a small host DTO
  (`application/dto/response/todoList/PeriodCompletionRecord.cs`) referenced only from
  `GetCompletionHistoryRoutineTimePeriodEndpoint` and `RoutineTimePeriodResponse` — both of which
  moved here — so it came along rather than staying behind as a single-consumer host leftover.

- **The freeze budget refills lazily, and nothing sweeps it.** There is no job behind
  `RoutineStreakFreezeService.RefreshFreezeBudget` — every surface that *shows* or *spends*
  `FreezesRemaining` has to call it first. Three sites do today: the grouped read, the nightly reset
  job, and `Apply` itself. A new read path that forgets it serves a stale count and refuses a freeze
  the user is owed, with an ordinary 400 and nothing in the logs. The window is a **calendar month**,
  not one period length — "two per period" would hand a daily routine two skips a day.

- **A freeze carries the run; it does not extend it.** `RecomputeStreak` walks the whole completion
  history oldest-first: a met period increments, an unfrozen miss zeroes, and a frozen one — like an
  item-less one — is skipped, so the run survives across it. If a freeze ever started incrementing,
  spending one would be worth more than actually doing the routine. It also only ever *raises*
  `Streak` / `BestStreak`: history is finite, a run can predate its oldest row, and the one operation
  the user spends a resource on to protect a streak must never be what cuts it down.

- **`StreakGraceDays` and the freeze budget are different leniencies and both stay.** Grace is
  time-based (finish late, keep the run) and expires on its own; a freeze is budgeted (skip entirely,
  keep the run). `Apply` clears `StreakGraceUntil` / `GraceNotifiedFor` **only** when the frozen entry
  is the newest one, because only that entry can own the open window — freezing an older miss leaves
  a live window alone.

- **The counts on a frozen completion row are left exactly as recorded.** A freeze changes how a
  period is *scored*, not what happened in it, so the completion rate stays honest and only the streak
  walk treats the period as neutral. `ck_routine_period_completion_frozen_at_matches_is_frozen` pins
  `IsFrozen` and `FrozenAt` together — a half-written pair reads as a freeze that never happened.

- **Two known-open correctness findings were deliberately NOT fixed during this move**: the reset job
  drops grace-expiry streak breaks; the two `TryReset` overloads (single-item vs. list) disagree on
  streak scoring; a failed notification aborts the nudge sweep's idempotency markers for a period
  mid-sweep in some code paths. Isolating this area with its own project and tests is *why* it was
  extracted early — fix in follow-up commits, not this one.

- **Background inserts have no authenticated user.** `RoutineTodoListResetJobHandler` and
  `RoutinePeriodNudgeJobHandler` both run unauthenticated (the Scheduler dispatcher fires them in a
  bare background scope); if either ever inserts
  an `IEntityWithUser` row it must set `UserId` explicitly, since `BaseWithUserEntitySaveChangesAsync`
  only fills it from an authenticated user. Neither job inserts today (only mutates existing tracked
  rows and adds `RoutinePeriodCompletion`, which is `BaseEntity`, not `IEntityWithUser`) — keep it
  that way, or add the explicit `UserId`.

- **Seeder reads use `IgnoreQueryFilters()`-equivalent explicit `UserId` predicates**, not the ambient
  query filter — `UserScoping` is on, so a seeder seeding a different user than the ambient one would
  otherwise read back zero rows.

- **`RoutineTimePeriodSeeder.Collides` checks two unique indexes**: `(user_id, text)` and
  `(user_id, length_in_days)`. Either one is enough to reject a row — don't simplify to a `Text`-only
  comparison.

- **Registering the slice with the host is four places, none of which break the build.**
  FastEndpoints `o.Assemblies` in `Program.cs` (missing → every routine route silently 404s);
  `ModuleServiceExtensions.ModuleAssemblies` (being in *both* that list and the
  `AddDependencyInjection` `AppDomain` sweep doubles every `IEnumerable<T>`, so each seeder and
  `RoutineTodoListActivityMembershipSource` registration runs/resolves twice, silently);
  `AppDbContext.ApplyHostConfigurations` (missing → the three tables vanish from the model); and the
  solution file. The two scheduled jobs are a **fifth** registration point: the handlers themselves come
  from the ordinary `IScopedService` scan, but their *schedules* only exist if
  `AddHostedService<RoutinesScheduledJobsRegistrar>()` is in `Program.cs`. Dropping that line builds
  fine — the jobs simply never fire.

- **Seeder `Order` is banded.** Routines owns **200–299**. See
  `AdhdTimeOrganizer.Core/infrastructure/persistence/seeder/SeederOrderBands.md` before adding a
  seeder anywhere in the solution.

- **The extraction produced an empty migration.** Table/column names come from the *class* name via
  `BaseEntityConfigure`, not the namespace, so moving `RoutineTimePeriod` et al. into this project's
  `Sydowwe.Framework`-derived namespace changed no schema. If a future slice extraction here produces
  a non-empty migration, check the diff before assuming you renamed a type — see the constraint-name
  gotcha documented in `AdhdTimeOrganizer.TodoLists/docs/summary.md`.

## Navigation

`docs/domain-map.md` is the index: what lives where, and which invariants are load-bearing.
