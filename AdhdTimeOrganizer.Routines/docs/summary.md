# AdhdTimeOrganizer.Routines — Agent Summary

**Purpose:** the routine domain — recurring to-do items (`RoutineTodoList`) grouped into time periods
(`RoutineTimePeriod`) with streaks, grace windows, budgeted streak freezes and a completion history
(`RoutinePeriodCompletion`); the reset/nudge domain logic (`RoutineResetService`) and the freeze logic
(`RoutineStreakFreezeService`); the three scheduled jobs that sweep them (keyed
`IScheduledJobHandler`s on the Scheduler module's substrate — this slice references no Quartz); and the
notification producer that turns a reset, a lead-time nudge, a grace warning, or the morning
suggested-today digest into a `Sydowwe.Framework.Contracts` notification.

## Bounded context

Owns: `RoutineTodoList`, `RoutineTimePeriod`, `RoutinePeriodCompletion`, `UserRoutineSettings` (the
per-user routine preferences behind `GET`/`PUT /routine/settings` — today just the weekly review-card
dismissal), their EF configurations, the 23 routine endpoints (time-period CRUD + select-options +
completion-history + streak-freeze, the to-do list CRUD + grouped-by-period + steps + toggle/reorder,
and the two settings endpoints), their DTOs and the five validators, `RoutineResetService`,
`RoutineStreakFreezeService`, `IRoutinePeriodNotificationService` / `RoutinePeriodNotificationService`,
the two Core seam implementations, and the `RoutineTimePeriodSeeder` / `RoutineTodoListSeeder` pair.

Tables live in the **`routines`** schema (`ModuleSchemas.cs`, keyed on this assembly).

Does **not** own — and must never reference: `AppDbContext`, `Program.cs`, the migrations, the DI
wiring, or Planning's `PlannerTask`. The completion fan-out into Planning goes through Core's
`RoutineTodoListIsDoneChangedEvent`, consumed by the host's `PlannerTaskIsDoneChangedEventHandler` —
that handler and its sibling `RoutineTodoListIsDoneChangedEventHandler` are host-side, so this project
has no knowledge of Planning at all.

## Dependency seams

- **References:** `AdhdTimeOrganizer.Core`, `AdhdTimeOrganizer.TodoLists`, `Sydowwe.Framework`,
  `Sydowwe.Framework.Contracts`. No host reference, by construction — see the comment in the csproj.
- **Referenced by:** `AdhdTimeOrganizer` (the host), for the hosted boot registrar
  (`AddHostedService<RoutinesScheduledJobsRegistrar>()`, which pushes the two job schedules to the
  Scheduler module) and the two call sites that still touch a routine item directly
  (`DesktopActivityHeartbeatEndpoint`, `PlannerTaskIsDoneChangedEventHandler`).
- **`Routines → TodoLists` is the one real outbound slice edge in the whole solution**, verified
  one-way: `RoutineTodoList` derives from TodoLists' `BaseTodoListItem`, and the routine endpoints
  subclass TodoLists' shared toggle/step/reorder endpoint bases. That is structural (inheritance /
  shared base types), not a membership-filter query, so it is **not** a candidate for the
  seam-inversion trick below — there is no `IQueryable` to hand back through an interface for "this
  type derives from that base." Nothing points back from TodoLists.
- **Three keyed Core seams are implemented here**, all in `application/seam/`:
  - `RoutineTodoListActivityMembershipSource` (`IActivityMembershipSource`) publishes "this activity is
    on a routine to-do list" without either project referencing the other. The facet is `TimePeriodId`.
    Its consumer was History's grid, deleted with `CQ-1`, so nothing reads it at present.
  - `RoutineTodoListActivityReferenceSource` (`IActivityReferenceSource`) exposes the `ActivityId`
    column to Core's activity-merge/delete machinery and repoints it on merge. **Streaks are carried
    across a merge untouched** — two routines on two duplicate activities stay two routines; merging
    says the activities were the same thing, not that the habits were. ⚠ Except **in the same time
    period**, where `(UserId, TimePeriodId, ActivityId)` is unique and two cannot both point at the
    survivor: the survivor's own routine wins and the duplicate's row (and its streak) is deleted. Where
    the survivor has no routine there, the oldest merged one is repointed instead.
  - `RoutineTodoListOwnedReferenceVerifier` (`IUserOwnedReferenceVerifier`) answers "is this routine
    item id the caller's?" for slices that store the id on their own rows — History's recordings
    today. It filters on the user id it is handed, not the ambient query filter, because this is a
    security answer rather than a scoped read.

  All three resolve **by string key**, not by type. A missing, misregistered or renamed key is silent — no
  build error, the consumer just quietly stops finding rows (for the verifier: starts refusing every
  write carrying a routine item id, since that one fails closed) — so any consumer needs a behavioural
  test asserting rows, not a route smoke test.

- **One Core seam is consumed here**: `ICalendarDayLookup` (implemented by Planning), read by
  `RoutineSuggestedTodayJobHandler` for the day's `WakeUpTime` and day type, and — through
  `GetAwayDatesAsync` — by `RoutineAwayDaysReader` (every reset site), the nudge sweep and
  `CalendarDayTypesChangedEventHandler`, for FO1's away-cycle excusal (see `domain-map.md`, *Away cycles*).
  Single-service resolution, so a missing registration throws at activation rather than silently falling
  back to the user's default wake-up or to "never away".
- **One Core event is consumed here**: `CalendarDayTypesChangedEvent`, published by Planning's two day-type
  writes. Its handler logs and swallows, so `RoutineAwayExcusalFlowTests` asserts on rows.

## Gotchas — things that will bite you

- **Window math is UTC; delivery is the user's.** Resets, the nudge windows (`EvaluateEndingSoon`,
  `ShouldWarnGraceExpiring`) and the `EndingSoonNotifiedFor` / `GraceNotifiedFor` marks are UTC end to
  end and must stay so. Only *when a due nudge reaches the user* is zone-aware: the nudge sweep runs every
  15 minutes and sends each window at the first wake-up + 60 min in `User.Timezone` that falls inside it
  (the day's calendar wake-up, or without a row the user's own default, `User.DefaultWakeUpTime` — FO10;
  the window's opening instant if no morning fits).
  It used to be one 09:00 UTC run for everyone. `RoutineSuggestedTodayJobHandler` works in
  `User.Timezone` throughout — "today" itself is local there — runs every 15 minutes, and marks
  `SuggestedTodayNotifiedFor` on **all** of a user's periods with an `ExecuteUpdate` straight after a
  successful send. Its "suggested for a day" rule
  (`RoutineSuggestedDay`) is duplicated on the client (`routineSuggestedDay.ts`) — change both or
  neither.

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

- **The freeze budget refills lazily, and nothing sweeps it.** There is no job behind
  `RoutineStreakFreezeService.RefreshFreezeBudget` — every surface that *shows* or *spends*
  `FreezesRemaining` has to call it first: the grouped read, the nightly reset job, `Apply` itself, and
  the four flat reads — those last go through `RoutineTimePeriodReadRefresher`, because they sit on the
  framework projection bases and an expression tree can neither refill a budget nor compute
  `NextResetAt`. A new read path that forgets it serves a stale count and refuses a freeze the user is
  owed, with an ordinary 400 and nothing in the logs. The window is a **calendar month**, not one
  period length — "two per period" would hand a daily routine two skips a day.

- **A freeze carries the run; it does not extend it.** `RecomputeStreak` walks the whole completion
  history oldest-first: a met period increments, an unfrozen miss zeroes, and a frozen one — like an
  item-less one — is skipped, so the run survives across it. If a freeze ever started incrementing,
  spending one would be worth more than actually doing the routine. It also only ever *raises*
  `Streak` / `BestStreak`: history is finite, a run can predate its oldest row, and the one operation
  the user spends a resource on to protect a streak must never be what cuts it down.

- **`StreakGraceDays` and the freeze budget are different leniencies and both stay.** Grace is
  time-based (finish late, keep the run) and expires on its own; a freeze is budgeted (skip entirely,
  keep the run). `Apply` clears `StreakGraceUntil` / `GraceNotifiedFor` **only** when the frozen entry
  is the newest one, because only that entry can own the open window — freezing an older miss leaves a
  live window alone. Which is exactly why the endpoint runs `CheckGrace` first: a window that has
  already *lapsed* is not live, and leaving it standing would let the next read zero the very streak
  the freeze just bought.

- **The counts on a frozen completion row are left exactly as recorded.** A freeze changes how a period
  is *scored*, not what happened in it, so the completion rate stays honest and only the streak walk
  treats the period as neutral. `ck_routine_period_completion_frozen_at_matches_is_frozen` pins
  `IsFrozen` and `FrozenAt` together — a half-written pair reads as a freeze that never happened.

- **The two `TryReset` overloads disagree on purpose.** The single-item overload (step toggle) freshens
  one item's checklist state but deliberately does **not** advance `LastResetAt`, write a
  `RoutinePeriodCompletion`, or evaluate the streak — only the list overload does, so a single-item
  path can never silently consume a reset cycle without scoring it. Don't "unify" them.

- **A nudge is marked only when a notification actually goes out.** A period that is already fully done
  when its lead-time window opens is skipped **unmarked**, so un-ticking an item tomorrow still earns
  the nudge. Marking on evaluation instead would look like a simplification and would silently drop
  that nudge.

- **Background inserts have no authenticated user.** `RoutineTodoListResetJobHandler`,
  `RoutinePeriodNudgeJobHandler` and `RoutineSuggestedTodayJobHandler` all run unauthenticated (the Scheduler dispatcher fires them in a
  bare background scope) — which is exactly what makes a sweep see every user, since the per-user query
  filter is inert without one. If either ever inserts an `IEntityWithUser` row it must set `UserId`
  explicitly, since `BaseWithUserEntitySaveChangesAsync` only fills it from an authenticated user.
  Neither inserts today (they mutate tracked rows and add `RoutinePeriodCompletion`, which is
  `BaseEntity`, not `IEntityWithUser`) — keep it that way, or add the explicit `UserId`.

- **The nudge sweep is sequential on purpose.** `Task.WhenAll` over the notify calls would run them
  concurrently against this scope's single `DbContext` — `INotificationService` writes its rows through
  the same scoped context — which throws rather than going faster.

- **Both sweeps page by user, and the page is also the release point.** `RoutineSweepPaging` reads the
  distinct user ids once and hands them out 100 at a time; each handler loads only that page's periods
  and calls `ChangeTracker.Clear()` at the end of it. Paged by *user*, never by row: the nudge sweep
  commits its "notified for" markers per page, so a user split across two pages could be half-notified.
  The clear is only safe once the page is both saved and announced — mid-page, a lost race is rolled
  back by `RoutineTodoListResetJobHandler.DiscardAsync` instead, because clearing there would detach the
  periods still to come and their saves would silently write nothing. The reset job's commit stays one
  per period; paging changed where the tracker is released, not how much a save carries.

- **Seeder reads use explicit `UserId` predicates**, not the ambient query filter — `UserScoping` is
  on, so a seeder seeding a different user than the ambient one would otherwise read back zero rows.

- **`RoutineTimePeriodSeeder.Collides` checks two unique indexes**: `(user_id, text)` and
  `(user_id, length_in_days)`. Either one is enough to reject a row — don't simplify to a `Text`-only
  comparison.

- **Registering the slice with the host is four places, none of which break the build.**
  FastEndpoints `o.Assemblies` in `Program.cs` (missing → every routine route silently 404s);
  `ModuleServiceExtensions.ModuleAssemblies` (being in *both* that list and the `AddDependencyInjection`
  `AppDomain` sweep doubles every `IEnumerable<T>`, so each seeder and each seam registration
  runs/resolves twice, silently); `AppDbContext.ApplyHostConfigurations` (missing → the tables vanish
  from the model); and the solution file. The two scheduled jobs are a **fifth** registration point:
  the handlers come from the ordinary `IScopedService` scan, but their *schedules* only exist if
  `AddHostedService<RoutinesScheduledJobsRegistrar>()` is in `Program.cs`. Dropping that line builds
  fine — the jobs simply never fire.

- **Seeder `Order` is banded.** Routines owns **200–299**. See
  `AdhdTimeOrganizer.Core/infrastructure/persistence/seeder/SeederOrderBands.md` before adding a seeder
  anywhere in the solution.

- **Class names are table names, namespaces are not.** Table/column names come from the *class* name via
  `BaseEntityConfigure`, so moving a type between folders or namespaces is not a migration — renaming
  one is. If a move here produces a non-empty migration, check the diff before assuming you renamed a
  type; see the constraint-name gotcha in `AdhdTimeOrganizer.TodoLists/docs/summary.md`.

- **Folders and namespaces have drifted.** The namespaces still carry a `todoList` segment
  (`…Routines.domain.model.entity.todoList`) that the folder layout no longer has
  (`domain/model/entity/`). Go by the paths in `domain-map.md`, not by the namespace.

## Deeper reference

- `domain-map.md` — model, invariants, business rules, navigation index
- `testing.md` — test strategy, the load-bearing guards, and the known gaps
- `README.md` — business intro and module-specific setup
