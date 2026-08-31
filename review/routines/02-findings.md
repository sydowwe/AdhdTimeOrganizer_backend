# Routines — Findings (02)

Point-in-time snapshot, 2026-08-31. Severity: 🔴 blocker · 🟠 important · 🟡 polish.

**Previously known, not re-flagged.** `summary.md:100-104` and `domain-map.md:62-64` record three
deliberately-deferred findings: the reset job dropping grace-expiry streak breaks; the two `TryReset`
overloads disagreeing on streak scoring; and a failed notification aborting the nudge sweep's
idempotency markers mid-sweep. The third looks **partially fixed** — `RoutinePeriodNudgeJobHandler.cs:56-88`
now wraps each period in a try/catch that preserves earlier markers, and
`RoutinePeriodNudgeJobTests.NudgeSweep_MidLoopNotifierFailure_...` pins it. The residue of that third
item is written up below as `CQ-6`, because it is a different mechanism (an unconditional marker write)
than the one the docs describe. The first two are untouched and stay out of this file.

---

## 5. Security & authorization

### `SEC-1` 🔴 No ownership check on `TimePeriodId` — a routine item can be attached to another user's period

`application/dto/request/CreateRoutineTodoListRequest.cs:9` · `application/dto/request/UpdateRoutineTodoListRequest.cs:22`
· `application/validator/CreateRoutineTodoListValidator.cs:34-63` · `application/validator/UpdateRoutineTodoListValidator.cs:44-73`

`TimePeriodId` arrives from the request body and is written straight onto the entity. The only code
that looks at it is the cross-field validator, which loads the period purely to read `LengthInDays`
for the `SuggestedDays` bucket rule — and when the lookup comes back empty it **returns without adding
a failure** (`:43-44`, `:53-54`). So a `TimePeriodId` that the caller does not own is not rejected
either way: if the ambient query filter hides it the validator sees `null` and waves it through, and
if it does not, the validator reads someone else's `LengthInDays`. Nothing downstream re-checks; the
FK is to `RoutineTimePeriod`, which is not user-scoped at the constraint level, and the unique index
`(UserId, TimePeriodId, ActivityId)` does not constrain ownership either.

**Why it matters.** The row lands with the attacker's `UserId` and the victim's `TimePeriodId`. The
attacker never sees it again (their own grouped read filters periods by `UserId`), so this is not an
information leak — it is a **write into the victim's data**. `RoutineTodoListResetJobHandler` runs
unauthenticated with the global filter inert (`infrastructure/jobs/RoutineTodoListResetJobHandler.cs:34-37`
and its own doc comment at `:17-19`), so it loads that foreign row into the victim's
`period.RoutineTodoListColl` and counts it: `completedCount / items.Count` in
`domain/service/RoutineResetService.cs:204-205`. An attacker can pin an always-incomplete item to a
victim's period and break a streak the victim cannot see the cause of, permanently, once per cycle.
The `RoutinePeriodCompletion` row written from those counts is also wrong forever.

**Fix.** Validate ownership explicitly in both validators — resolve the period with an
`&& p.UserId == <caller>` predicate and `AddFailure` when it is null, rather than returning. Getting
the caller id into a FluentValidation validator here is awkward because these already reach for a
fresh scope (see `CQ-11`); injecting `ILoggedUserService` alongside is the smaller change. Belt and
braces: re-assert ownership in `AfterMapping` on both endpoints, which is where a future
`ToEntity`/`UpdateEntity` refactor would otherwise reintroduce this.

`Doc impact: domain-map.md → Invariants (add: "TimePeriodId on a routine item is caller-supplied and must be ownership-checked; the FK and the global filter do not do it")`

### `SEC-2` 🟠 `PUT /routine-todo-list/{id}` flips `IsDone` without the toggle logic or the cross-slice event

`application/dto/request/UpdateRoutineTodoListRequest.cs:16`

`UpdateEntity` assigns `e.IsDone = IsDone` directly. Every other path to that field goes through
`BaseTodoListItem.SetDone` (which reconciles `DoneCount` against `TotalCount` and the steps) and then
through `RoutineToggleIsDoneTodoListEndpoint`, which calls `RoutineResetService.UpdateItemStreak` and
publishes `RoutineTodoListIsDoneChangedEvent` (`application/endpoint/routineTodoList/command/RoutineToggleIsDoneTodoListEndpoint.cs:55-75`).
The `PUT` does none of that.

**Why it matters.** That event is the *only* channel by which Planning's `PlannerTaskIsDoneChangedEventHandler`
learns a routine was completed — `summary.md:28-34` is explicit that the fan-out is entirely
event-based and that this slice has no other link to Planning. A client that completes a routine via
the update endpoint leaves the paired planner task stale, silently and permanently, and leaves
`DoneCount` inconsistent with the steps. Cross-slice handlers log-and-swallow, so nothing surfaces.

**Fix.** Either drop `IsDone` from `UpdateRoutineTodoListRequest` (the toggle endpoint is the
supported path, and the SPA uses it), or route the assignment through `SetDone` and raise the event in
the endpoint's `AfterMapping`. Dropping it is cleaner and matches how `RoutineTimePeriodRequest`
deliberately withholds `Streak` / `LastResetAt` from the request (`RoutineTimePeriodRequest.cs:85-87`).

`Doc impact: domain-map.md → Business rules (state that IsDone is toggle-only and why)`

### `SEC-3` 🟠 Two reset sites race on the unique `(TimePeriodId, PeriodStart)` index

`application/endpoint/routineTodoList/query/GetAllGroupedRoutineTodoListEndpoint.cs:63-67` ·
`application/endpoint/routineTodoList/command/RoutineToggleIsDoneTodoListEndpoint.cs:45` ·
`infrastructure/jobs/RoutineTodoListResetJobHandler.cs:67-68` ·
`infrastructure/persistence/configuration/RoutinePeriodCompletionConfiguration.cs:31`

Three separate paths apply a lazy reset and insert a `RoutinePeriodCompletion`, and the completion row
is uniquely keyed on `(TimePeriodId, PeriodStart)`. None of them takes a lock, re-reads, or handles a
unique violation. Two browser tabs hitting the grouped read within the same second — or a grouped read
landing while the 02:00 sweep is mid-flight — both compute the same elapsed period and both insert.

**Why it matters.** The loser gets an unhandled `DbUpdateException`. On the grouped read that is a
failed page load for the user; on the nightly job it aborts the **whole remaining sweep**, because the
insert is a single `SaveChangesAsync` over every period's completions
(`RoutineTodoListResetJobHandler.cs:67`) — one collision and no later period in the batch resets that
night. `DisallowConcurrent = true` on the registration guards job-vs-job, not job-vs-request.

**Fix.** Catch `PostgresErrorCodes.UniqueViolation` around the completion insert and treat it as
"someone else already closed this period" — the mutation is idempotent by construction, so swallowing
is correct here. The framework already has the shape:
`RetryDbConcurrencyHelper.IsUniqueViolation` / `DbUtils.HandleException`. Doing the job's inserts
per-period rather than one batched `SaveChanges` would additionally stop one collision from taking the
sweep down.

The same shape exists, far less dangerously, at
`application/endpoint/routineSettings/UpdateRoutineSettingsEndpoint.cs:36-43`: concurrent first-time
`PUT`s both find no row and both insert against a unique `user_id`. One request 500s. Low likelihood,
same one-line fix.

`Doc impact: none`

### `SEC-4` 🟡 Check-constraint violations return the raw Postgres message to the client

`infrastructure/persistence/configuration/RoutineTimePeriodConfiguration.cs:25-72` (via
`framework/Sydowwe.Framework/infrastructure/persistence/DatabaseExceptionUtils.cs:30-31`)

`CheckViolation` maps to `ValidationError` with `pgEx.Message` as the detail — which includes the
constraint name and, for a check violation, Postgres's rendering of the offending row. For this module
that row is routine metadata rather than anything sensitive, so the severity is low; it is listed
because the recent commit `fddba8a` ("stop leaking exception text") shows this is a live concern in
the repo, and because `CQ-3` means these constraints are reachable from ordinary input.

`Doc impact: none`

---

## 6. Code quality

### `CQ-1` 🔴 Monthly and yearly anchored periods never reset

`domain/service/RoutineResetService.cs:36-76`

Both calendar-aligned branches end in a catch-up loop:

```csharp
var candidate = MonthlyCandidate(year, month, targetDay);
while (candidate < now) { month++; …; candidate = MonthlyCandidate(year, month, targetDay); }
return candidate;
```

The loop's postcondition is `candidate >= now`. `MonthlyCandidate` always returns **midnight UTC**
(`:100`). Every caller then asks `if (now < nextReset) return null;` (`:173`, `:197`). So a reset can
only fire when `now` is exactly midnight to the tick — and the nightly job runs at **02:00**
(`infrastructure/scheduling/RoutinesScheduledJobsRegistrar.cs:28`), by which point the candidate for
today has already been skipped to next month.

**A `LengthInDays` of 30 or 365 with a non-zero `ResetAnchorDay` therefore never resets, ever.** The
weekly-aligned branch (`:20-28`) does not have this problem: it returns `earliest + daysUntil` derived
from `LastResetAt`, without consulting `now`.

**Why it matters, and why nobody noticed.** `RoutineTimePeriodSeeder.Defaults`
(`infrastructure/persistence/seeder/userDefault/RoutineTimePeriodSeeder.cs:21-22`) gives **every user**
a "Monthly" (30d, anchor 1) and a "Yearly" (365d, anchor 1) period out of the box. For those periods:
items are never uncticked, `LastResetAt` stays null forever, no `RoutinePeriodCompletion` row is ever
written, the streak is never evaluated, and the completion-history heatmap stays permanently empty. It
is silent in every direction — `NextResetAt` projects a perfectly plausible future date (that is the
loop's return value), and the nudge path still fires because `EvaluateEndingSoon` only needs
`now < nextReset` (`:128`). The user sees reminders about a period that never turns over.

**Fix.** The loop is trying to solve "the routine went dormant for several cycles", but it does it
against `now` instead of against `lastReset`, which conflates "find the next boundary" with "find a
boundary in the future". Advance from `lastReset` until the candidate is strictly after `lastReset`
(not after `now`), and let the caller's `now < nextReset` comparison decide whether it is due — which
is exactly what the weekly branch does. Note this makes catch-up one cycle per invocation, same as the
weekly path; see `CQ-8`. Pair the fix with `TEST-1`, whose absence is what let this ship.

`Doc impact: domain-map.md → Domain logic (ComputeNextReset row: state that the returned instant is derived from LastResetAt and may be in the past — that is what makes a period due)`

### `CQ-2` 🟠 Activity merge throws when two routines collide in the same period

`application/seam/RoutineTodoListActivityReferenceSource.cs:35-45` ·
`infrastructure/persistence/configuration/RoutineToDoListConfiguration.cs:53-54`

`RepointAsync` rewrites `ActivityId` on every routine referencing a merged activity, with no collision
handling. `RoutineTodoList` carries a unique index on `(UserId, TimePeriodId, ActivityId)`. If a user
has a routine on activity A and another on activity B **in the same time period**, merging B into A
sets both rows to `ActivityId = A` and the index rejects the save.

The class's own remark asserts the opposite outcome — "Two routines on two duplicate activities stay
two routines pointing at one activity" (`:19-21`) — and gives a good reason for it (folding two
streaks would have to invent a rule). That reasoning is sound; it just is not what the schema permits
in the same-period case. Both sides are wrong, so this is also `DOC-3`.

**Why it matters.** Activity merge is a user-facing operation that fans out across six slices in one
transaction (`ActivityMergeTests:82-88`). A `DbUpdateException` here fails the entire merge, not just
the routine part. `ActivityMergeTests` seeds exactly one routine on the duplicate, one short of the
failing case (`TEST-2`).

**Fix.** Decide the collision rule and implement it — the least surprising is: when the survivor
already has a routine in that period, delete the duplicate's row and keep the survivor's (its streak
is the one the user has been looking at), and say so in the remark. Detect it by grouping the loaded
rows on `(UserId, TimePeriodId)` before assigning.

`Doc impact: domain-map.md → Invariants (add the merge collision rule); fix the XML remark on RoutineTodoListActivityReferenceSource`

### `CQ-3` 🟠 `RoutineTimePeriodValidator` does not mirror two of its DB constraints

`application/validator/RoutineTimePeriodValidator.cs:14-15` (and no `HistoryDepth` rule anywhere in the file)

- `LengthInDays` has `GreaterThanOrEqualTo(1)` but no upper bound; the DB check is `1..365`
  (`RoutineTimePeriodConfiguration.cs:31-32`).
- `HistoryDepth` has **no rule at all**; the DB check is `1..100` (`:50-52`) and the request's own XML
  doc claims "1–100" (`RoutineTimePeriodRequest.cs:25`).

Every other constraint in that configuration has a matching FluentValidation rule, each with a comment
saying it mirrors the check — these two were missed. Because `DbUtils` maps a check violation to a
400, the user-visible symptom is mild (a 400 with a Postgres message instead of a field-keyed
validation error), which is precisely why the existing tests did not catch it — see `TEST-4`.

**Fix.** `RuleFor(x => x.LengthInDays).InclusiveBetween(1, 365);` and
`RuleFor(x => x.HistoryDepth).InclusiveBetween(1, 100);`.

`Doc impact: none`

### `CQ-4` 🟠 Updating a routine item destroys and recreates all of its steps

`application/endpoint/routineTodoList/command/UpdateRoutineTodoListEndpoint.cs:20-27`

`AfterMapping` replaces `entity.Steps` wholesale with freshly constructed `TodoListStep` objects. It
carefully carries `IsDone` across by looking up the old step by `s.Id` — but assigns it to a **new**
object with no `Id`, so EF deletes every existing step row and inserts replacements with new keys.

**Why it matters.** Step identity churns on every save of the parent, even an unrelated one (a note
edit re-keys every step). Any client holding step ids has them invalidated; `ToggleStepIsDoneRoutineTodoListEndpoint`
addresses steps by id, so a concurrent step toggle against a just-updated item 404s. It also generates
delete+insert traffic proportional to the step count for a no-op edit.

**Fix.** Reconcile in place: update steps whose `Id` matches, add those without one, remove those
absent from the request. `UpdateRoutineTodoListValidator` already models steps as having optional ids,
so the request shape supports it.

`Doc impact: none`

### `CQ-5` 🟠 Read endpoints serve a malformed `RoutineTimePeriodResponse`

`application/dto/response/RoutineTimePeriodResponse.cs:57-78` · `application/dto/response/RoutineToDoListResponse.cs:63-79`
· `application/endpoint/routineTimePeriod/query/GetAllRoutineTimePeriodEndpoint.cs` ·
`application/endpoint/routineTimePeriod/query/GetByIdRoutineTimePeriodEndpoint.cs`

`RoutineTimePeriodResponse.From` exists precisely so that the grouped read and the freeze endpoint
cannot drift, and its own doc says so (`:39-46`). But `Projection` — the raw expression `From` wraps —
is what the framework's `BaseGetAll` / `BaseGetById` bases call directly. Anything served through those
bases gets `NextResetAt = default(DateTime)` (`0001-01-01`) and an empty `CompletionHistory`.

Worse, the nested period inside `RoutineTodoListResponse.Projection` (`:63-79`) omits
`FreezesRemaining`, `FreezeBudget` and `FreezeBudgetResetsAt` entirely, so they serialize as `0`, `0`,
`null`. `RoutineTimePeriodResponse` documents the contract the SPA relies on: freezes are "always a
number once the server supports freezes — the client reads `null` as 'this server has no freeze
feature'" (`:23-27`). Serving a hard `0` tells the client the user has no freezes left rather than
that the feature is absent, so the affordance renders disabled instead of hidden.

This also breaks `domain-map.md`'s invariant 5 (`DOC-4`): `GET /routine-time-period` and
`GET /routine-time-period/{id}` display `FreezesRemaining` without ever calling `RefreshFreezeBudget`,
so they can serve a stale, unrefilled count.

**Fix.** Have the two `GetX` endpoints override the projection hook to post-process through
`From(period, history, now)` — or, if the base does not admit that, hand-write them as the grouped read
already is. Add the three freeze fields to the nested projection in `RoutineTodoListResponse`, or drop
the nested period down to the identifying fields the client actually uses there.

`Doc impact: domain-map.md → Invariants (5) — either the invariant gains an exception for the list reads, or, better, the code is fixed and the invariant stands`

### `CQ-6` 🟠 The grace warning is marked as sent even when it failed

`infrastructure/jobs/RoutinePeriodNudgeJobHandler.cs:71-76` ·
`application/service/RoutinePeriodNotificationService.cs:55-62` · `:69-84`

The two notification paths in the sweep are asymmetric. `NotifyEndingSoonAsync` returns `bool` and the
sweep only stamps `EndingSoonNotifiedFor` when it comes back true (`:64-68`) — deliberate, documented,
tested. `NotifyGraceExpiringAsync` returns bare `Task`, and `SafeNotifyAsync` swallows every exception
(`:80-83`), so the sweep stamps `GraceNotifiedFor = period.StreakGraceUntil` unconditionally at `:74`
whether or not anything was delivered.

**Why it matters.** `ShouldWarnGraceExpiring` is keyed on that mark (`RoutineResetService.cs:141`), so
a transient SignalR/SMTP failure permanently suppresses the warning for that grace window. The window
then lapses and `CheckGrace` breaks the streak with no warning ever having reached the user — the exact
outcome the notification exists to prevent, reached silently.

**Fix.** Make `NotifyGraceExpiringAsync` return `Task<bool>` like its sibling and gate the marker on
it. The interface doc already promises best-effort semantics for both; this just makes "was it
actually raised" observable for the second one.

`Doc impact: domain-map.md → Known-open findings (this is the residue of the third deferred item; re-scope or remove that bullet once fixed)`

### `CQ-7` 🟠 Item streak inflates on untick/retick within one period

`domain/service/RoutineResetService.cs:258-267`

`UpdateItemStreak` increments unconditionally whenever the item is done, and is called from both
toggle endpoints (`RoutineToggleIsDoneTodoListEndpoint.cs:57`,
`ToggleStepIsDoneRoutineTodoListEndpoint.cs:37`). Nothing keys the increment to the period — so
toggling an item off and on again in the same cycle increments `Streak` and `BestStreak` each time. A
user can hold a daily routine's streak counter and watch it climb.

The period-level streak does not have this problem: it is advanced once per reset in the list
`TryReset` (`:209`). Only the per-item counters are affected.

**Fix.** Guard on `LastResetDate` / `LastCompletedAt` — increment only if the item has not already been
counted for the current cycle (`item.LastCompletedAt < period.LastResetAt`, or compare
`DateOnly.FromDateTime(item.LastCompletedAt)` against `item.LastResetDate`). The entity already carries
both fields.

`Doc impact: domain-map.md → Domain logic (UpdateItemStreak row: note it is once-per-cycle, not once-per-toggle)`

### `CQ-8` 🟠 A dormant period catches up one cycle per invocation

`domain/service/RoutineResetService.cs:236`

`TryReset` sets `period.LastResetAt = nextReset` — the *next* boundary after the last one, not the
current time. With a daily nightly job, a period untouched for sixty cycles needs sixty job runs to
become current, writing one backdated `RoutinePeriodCompletion` per run and re-evaluating the streak
each time from items that have been sitting unticked throughout.

**Why it matters.** For a daily routine, "I was away for two months" produces two months of
retroactive misses arriving one per day, each potentially firing an end-of-period notification
(`RoutineTodoListResetJobHandler.cs:74-75`) — a drip of "you missed Monday" for weeks. The grouped read
hits the same path, so opening the app once resolves exactly one cycle. Note this is separate from
`CQ-1`: fixing that one converts monthly/yearly periods from *never* resetting to resetting on this
same drip schedule, so the two want fixing together.

**Fix.** Loop `TryReset` per period until it returns null (bounded — cap at, say, `HistoryDepth`
iterations and jump `LastResetAt` forward past the remainder), and collapse the skipped cycles into a
single notification. The completion rows for skipped cycles are arguably worth writing (the heatmap
should show the gap); the notifications are not.

`Doc impact: domain-map.md → Domain logic (TryReset list overload: document the catch-up semantics)`

### `CQ-9` 🟡 `RoutinePeriodCompletionConfiguration` hand-rolls its key

`infrastructure/persistence/configuration/RoutinePeriodCompletionConfiguration.cs:10`

`builder.HasKey(x => x.Id)` with no `BaseEntityConfigure()` call. CLAUDE.md is explicit: configurations
"always use the builder extension helpers — call `BaseEntityConfigure()` first, and never hand-roll
`ToTable` / `HasKey` / row_version / timestamps." The other three configurations in this module comply
(`RoutineTimePeriodConfiguration` via `BaseTextColorEntityConfigure`, and the other two directly).

The entity is a plain `BaseEntity` and defines its own `CreatedAt`, so this currently works — but it
opts out of whatever the helper standardises (row_version among them), which means this table will
silently miss any future convention added there.

**Fix.** Replace with `builder.BaseEntityConfigure();` and drop the redundant `HasKey`. Verify the
generated migration is empty before assuming it is a no-op.

`Doc impact: none`

### `CQ-10` 🟡 Two dead filter DTOs, both exposing a settable `UserId`

`application/dto/filter/RoutineToDoListFilterRequest.cs` · `application/dto/filter/RoutineTimePeriodFilterRequest.cs`

Neither type is referenced anywhere in the solution (the only other hit is
`review/portal/REMAINING.txt`, which lists one of them as un-reviewed). The module has no grid or
filter endpoint. Both records expose `public long? UserId { get; set; }` — harmless while unused, but
it is a mass-assignment shape waiting for someone to wire up a `BaseGridEndpoint` against it and hand
a caller a `userId` filter parameter.

**Fix.** Delete both. If a routine grid is planned, reintroduce them without `UserId` — scoping is the
DbContext's job here (invariant 3).

`Doc impact: domain-map.md → the DTO paragraph mentions "two filters"; drop it`

### `CQ-11` 🟡 Validators open a DI scope per validation, and duplicate 30 lines

`application/validator/CreateRoutineTodoListValidator.cs:34-63` ·
`application/validator/UpdateRoutineTodoListValidator.cs:44-73`

Both take `IServiceScopeFactory` and build a fresh `AsyncScope` to resolve a `DbContext` for one
scalar lookup. FastEndpoints validators are resolved from the request scope; injecting `DbContext`
directly is simpler, shares the request's transaction and — relevant to `SEC-1` — keeps the ambient
user context rather than resolving a context whose `ScopeUserId` provenance is a fresh scope's
business. The 30-line `SuggestedDays`/`SuggestedDayOfMonth` bucket rule is then duplicated verbatim
between the two files, so a change to the bucket boundaries has to land twice.

**Fix.** Inject `DbContext`; extract the bucket rule into a shared private static helper or a small
`AbstractValidator` base the two derive from.

`Doc impact: none`

### `CQ-12` 🟡 Inconsistent user-scoping style across endpoints

The module mixes three approaches: explicit `x.UserId == loggedUserId` predicates
(`GetAllGroupedRoutineTodoListEndpoint.cs:32`, `SpendStreakFreezeRoutineTimePeriodEndpoint.cs:57`,
`GetCompletionHistoryRoutineTimePeriodEndpoint.cs:31`, `CreateStepRoutineTodoListEndpoint.cs:13`);
explicit reliance on the global filter with an `AuthorizeAsync` override returning `true` plus a
comment (`GetByIdRoutineTodoListEndpoint.cs:11-12`, `GetByIdRoutineTimePeriodEndpoint.cs:11-12`); and
silent reliance with no comment (`RoutineToggleIsDoneTodoListEndpoint.cs:24-37`).

All three are correct today and invariant 3 sanctions the filter-only form. The problem is that a
reader cannot tell "deliberately relying on the filter" from "forgot", which is how `SEC-1` reads at a
glance. The freeze endpoint's comment (`:54-55`) is the right model.

**Fix.** Comment the filter-only sites, or standardise on the explicit predicate for anything that
takes an id from the request.

`Doc impact: none`

---

## 9. Performance hotspots

| Where | Issue | Impact | Mitigation | ID |
|---|---|---|---|---|
| `GetAllGroupedRoutineTodoListEndpoint.cs:76-91` | Loads the **entire** completion history for every period, then trims to `HistoryDepth` in memory | History grows one row per period per cycle forever; a two-year-old daily routine is ~730 rows fetched to render 16. Grows unboundedly with account age, on the module's most-hit read | `PARTITION BY … ORDER BY period_start DESC` in SQL, or per-period `Take(HistoryDepth)` — the pattern `GetCompletionHistoryRoutineTimePeriodEndpoint.cs:42-47` already uses correctly | `PERF-1` |
| `GetAllGroupedRoutineTodoListEndpoint.cs:102` | `RoutineTodoListResponse.Projection(new[] { e }.AsQueryable()).Single()` — per item | Builds and interprets the whole projection expression tree once **per item** via LINQ-to-Objects, on a request already holding the materialised graph | Project the list once outside the loop, or map with a plain function. The expression is only needed for the `IQueryable` path | `PERF-2` |
| `RoutineTodoListResetJobHandler.cs:34-37` | Loads every user's periods with `RoutineTodoListColl` + `Steps` into memory in one go | Whole-table materialisation with two levels of collection include; memory scales with total users × routines × steps. Also makes the single `SaveChangesAsync` at `:68` an all-or-nothing batch (see `SEC-3`) | Page by user id and commit per page | `PERF-3` |
| `RoutinePeriodNudgeJobHandler.cs:40-43` | Same shape, plus `Where(p => !p.IsHidden)` with no supporting index | Sequential scan of `routine_time_period` daily. Small today; the sequential-notify loop below it is deliberate and documented, so this is the only part worth changing | Page by user; see index below | `PERF-4` |
| `RoutineStreakFreezeService.RecomputeStreak` via `SpendStreakFreezeRoutineTimePeriodEndpoint.cs:77-80` | Loads the complete history to recompute | **Deliberate and correct** — a truncated history under-counts the run, and the code says so. Listed only so it is not "found" again later; the growth is bounded by `PERF-1`'s fix, not this one | none | — |

### Indexes that should exist

- `routine_period_completion (time_period_id, period_start DESC)` — the unique index on
  `(time_period_id, period_start)` already serves ordered lookups by period, so this is only worth
  adding if `PERF-1` is fixed with a windowed query that wants the descending order. **Probably no
  action.**
- `routine_time_period (is_hidden)` or a partial index `WHERE NOT is_hidden` — supports the nudge
  sweep's filter (`PERF-4`). Marginal at current scale; add with `PERF-3`'s paging, not before.
- **No missing index on the hot read path.** `routine_todo_list` already has
  `(user_id, time_period_id)` as an explicit performance index (`RoutineToDoListConfiguration.cs:57`)
  and the unique `(user_id, time_period_id, activity_id)` above it. The grouped read's cost is `PERF-1`
  and `PERF-2`, not index coverage — worth stating plainly so nobody "fixes" it with an index.

---

## Code/doc drift

### `DOC-1` 🟡 Every folder path in the docs is stale

`domain-map.md:25` says entities live in `domain/model/entity/todoList/`; they are in
`domain/model/entity/`. `:34` says `domain/service/RoutineResetService.cs` — correct. `:66` says
`application/service/routine/`; it is `application/service/`. `:74-82` prefixes the configurations with
`configuration/todoList/`; they are in `configuration/`. `:84` says the endpoints are under
`application/endpoint/todoList/`; they are under `application/endpoint/{routineTimePeriod,routineTodoList,routineSettings}/`.
`:110` says DTOs are under `request/todoList` / `response/todoList`; they are under `request/` /
`response/`.

The **namespaces** still contain `.todoList` (e.g. `AdhdTimeOrganizer.Routines.domain.model.entity.todoList`),
which is presumably where the drift came from — the slice extraction flattened the directories without
renaming namespaces. Harmless to the compiler; actively misleading to the navigation index, which is
the document's stated job.

Fix the doc. Optionally realign namespaces to folders in a separate commit — that is a pure rename with
no schema impact (`summary.md:137-141`).

`Doc impact: domain-map.md → every path reference in Entities, Notifications, Configuration, HTTP surface and the DTO paragraph`

### `DOC-2` 🟡 Endpoint count disagrees between the two docs

`summary.md:20` says "the 21 routine endpoints". `domain-map.md:87-91` tabulates 9 + 9 + 3 + 2 = **23**,
which matches the tree (5 time-period commands including the freeze, 4 time-period queries, 6 to-do
commands, 3 to-do queries, 3 step endpoints, 2 settings). The summary is stale — it predates the two
settings endpoints.

`Doc impact: summary.md → Bounded context (21 → 23)`

### `DOC-3` 🟠 The merge remark asserts behaviour the schema forbids

`application/seam/RoutineTodoListActivityReferenceSource.cs:19-21` — see `CQ-2`. Both the code and the
comment need changing; the comment's *reasoning* (don't invent a streak-folding rule) should survive
into whatever collision rule is chosen.

`Doc impact: domain-map.md → Invariants (new merge-collision rule)`

### `DOC-4` 🟠 Invariant 5 does not hold

`domain-map.md:152-154` states that every path that shows or spends `FreezesRemaining` calls
`RefreshFreezeBudget` first, and `summary.md:75-80` names the three sites that do. `GET /routine-time-period`
and `GET /routine-time-period/{id}` also show it (via `RoutineTimePeriodResponse.Projection`) and
refresh nothing — see `CQ-5`. The invariant is the right rule; the code is what is wrong.

`Doc impact: domain-map.md → Invariants (5) once CQ-5 is fixed, the invariant stands unchanged — no doc edit needed, which is the point of fixing the code instead`

### `DOC-5` 🟡 A test name contradicts the documented `TryReset` contract

`RoutineResetServiceTests.cs:204` — `TryReset_Single_AfterResetTime_ResetsItemAndUpdatesPeriod`. The
single-item overload explicitly does not update the period (`RoutineResetService.cs:161-169`), and the
docs restate that at `domain-map.md:42`. The test name asserts the opposite and the body contains a
vacuous assertion — see `TEST-10`. Rename to `..._ResetsItemWithoutAdvancingLastResetAt` and assert
that `LastResetAt` is **unchanged**, which is the actual guarantee and is currently unpinned.

`Doc impact: none (fix the test)`
