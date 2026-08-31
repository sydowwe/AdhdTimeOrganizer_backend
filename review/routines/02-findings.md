# Routines — Findings (02)

Point-in-time snapshot, **re-verified 2026-08-31** (second pass, deep). Severity: 🔴 blocker ·
🟠 important · 🟡 polish.

Every finding below was re-read against the source this pass; every `file:line` was re-checked. Where
the first pass was wrong, the correction is recorded in **Corrections to the first pass** at the end
rather than silently dropped.

**Previously known, not re-flagged.** `docs/testing.md`'s "Known gaps" list carries four items — the
export gap on `RoutineTimePeriod` / `UserRoutineSettings`, `RoutinePeriodCompletion` having no direct
per-user reachability test, no assertion on the two jobs' cron cadence, and the single-item `TryReset`
asymmetry being pinned only at unit level. Those are decisions, not defects, and stay out of this file
except where a finding below happens to touch one.

---

## 5. Security & authorization

### `SEC-1` 🔴 No ownership check on `TimePeriodId` — a routine item can be attached to another user's period

`application/dto/request/CreateRoutineTodoListRequest.cs:9,20` ·
`application/dto/request/UpdateRoutineTodoListRequest.cs:9,22` ·
`application/validator/CreateRoutineTodoListValidator.cs:34-63` ·
`application/validator/UpdateRoutineTodoListValidator.cs:44-73`

`TimePeriodId` arrives in the request body and is written straight onto the entity (`ToEntity` at
`CreateRoutineTodoListRequest.cs:20`, `UpdateEntity` at `UpdateRoutineTodoListRequest.cs:22`). The only
code that reads it is the cross-field validator, which loads the period solely to get `LengthInDays`
for the `SuggestedDays` bucket rule — and when the lookup returns nothing it **returns without adding a
failure** (`Create…:43-44`, `Update…:53-54`).

Both validators resolve their `DbContext` out of a **fresh** `IServiceScopeFactory` scope
(`Create…:36-37`), so whether the period is even visible depends on how `ScopeUserId` is populated in
that new scope — the one place in the module where the answer is not obvious. Either way the outcome is
the same: a `TimePeriodId` the caller does not own is never rejected. Nothing downstream re-checks. The
FK targets `RoutineTimePeriod`, which is not user-scoped at the constraint level
(`RoutineTodoListConfiguration.cs:47-50`), and the unique index `(UserId, TimePeriodId, ActivityId)`
(`:53-54`) does not constrain ownership either.

**Why it matters.** The row lands with the attacker's `UserId` and the victim's `TimePeriodId`. The
attacker never sees it again — their own grouped read filters periods by `UserId`
(`GetAllGroupedRoutineTodoListEndpoint.cs:32`) — so this is not a read leak, it is a **write into the
victim's data**. `RoutineTodoListResetJobHandler` runs unauthenticated with the global filter inert
(`:34-37`, and its own doc comment at `:16-19`), so it pulls that foreign row into the victim's
`period.RoutineTodoListColl` and counts it: `completedCount / items.Count` at
`domain/service/RoutineResetService.cs:204-205`. An attacker can pin a permanently-incomplete item to a
victim's period and break a streak the victim cannot see the cause of, once per cycle, forever. The
`RoutinePeriodCompletion` row written from those counts is wrong permanently too — and it is the input
to `RecomputeStreak`, so a later streak freeze re-derives from corrupted history.

**Fix.** Resolve the period with an `&& p.UserId == <caller>` predicate and `AddFailure` when it comes
back null, in both validators. Getting the caller id in is awkward only because these already build
their own scope (see `CQ-11`); injecting `ILoggedUserService` alongside is the smaller change, and
injecting the request-scoped `DbContext` instead of `IServiceScopeFactory` fixes both at once. Belt and
braces: re-assert ownership in `AfterMapping` on both endpoints
(`CreateRoutineTodoListEndpoint.cs:25`, `UpdateRoutineTodoListEndpoint.cs:18`), which is where a future
mapping refactor would otherwise reintroduce it.

`Doc impact: domain-map.md → Invariants (add: "TimePeriodId on a routine item is caller-supplied; neither the FK nor the global filter checks that the caller owns it — the validators must")`

### `SEC-2` 🟠 `PUT /routine-todo-list/{id}` writes `IsDone` / `DoneCount` / `DisplayOrder` raw, skipping the toggle logic and the cross-slice event

`application/dto/request/UpdateRoutineTodoListRequest.cs:16-18,22`

`UpdateEntity` assigns `e.IsDone`, `e.DoneCount` and `e.DisplayOrder` directly from the body. Every
other path to `IsDone` goes through `BaseTodoListItem`'s toggle logic
(`AdhdTimeOrganizer.TodoLists/application/endpoint/BaseToggleIsDoneTodoListEndpoint.cs:73-126`, which
reconciles `DoneCount` against `TotalCount` **and the steps**) and then through
`RoutineToggleIsDoneTodoListEndpoint`, which calls `RoutineResetService.UpdateItemStreak` (`:55-58`)
and publishes `RoutineTodoListIsDoneChangedEvent` (`:71-75`). The `PUT` does none of it.

**Why it matters.** That event is the *only* channel by which Planning's
`PlannerTaskIsDoneChangedEventHandler` learns a routine was completed — `summary.md`'s "Bounded
context" section is explicit that the fan-out is entirely event-based and that this slice has no other
link to Planning. A client completing a routine via the update endpoint leaves the paired planner task
stale, silently and permanently, and leaves `IsDone` / `DoneCount` / the step flags mutually
inconsistent. Cross-slice handlers log-and-swallow, so nothing surfaces. `DisplayOrder` is likewise
derived server-side on create (`CreateRoutineTodoListEndpoint.cs:27`) and then freely overwritable here.

**Fix.** Drop `IsDone`, `DoneCount` and `DisplayOrder` from `UpdateRoutineTodoListRequest` — the toggle
and reorder endpoints are the supported paths and the SPA uses them. That is exactly what
`RoutineTimePeriodRequest` already does for the period's derived fields, and it says why in a comment
worth copying (`RoutineTimePeriodRequest.cs:85-87`). If `IsDone` must stay, route it through the toggle
logic and raise the event in `AfterMapping`.

`Doc impact: domain-map.md → Business rules (state that IsDone/DoneCount/DisplayOrder on a routine item are server-owned, and why)`

### `SEC-3` 🟠 Three reset sites race on the unique `(TimePeriodId, PeriodStart)` index, and none of the three handles the collision

`application/endpoint/routineTodoList/query/GetAllGroupedRoutineTodoListEndpoint.cs:63-67` ·
`application/endpoint/routineTodoList/command/RoutineToggleIsDoneTodoListEndpoint.cs:45` ·
`infrastructure/jobs/RoutineTodoListResetJobHandler.cs:67-68` ·
`infrastructure/persistence/configuration/RoutinePeriodCompletionConfiguration.cs:31`

Three paths apply a lazy reset and insert a `RoutinePeriodCompletion`, which is uniquely keyed on
`(TimePeriodId, PeriodStart)`. None takes a lock, re-reads, or handles a unique violation. Two browser
tabs hitting the grouped read in the same second — or a grouped read landing while the 02:00 sweep is
mid-flight — both compute the same elapsed period and both insert.

**Why it matters, per site:**

- **Grouped read** (`:66`) — `HandleAsync` has no try/catch, so the loser gets an unhandled 500 on the
  module's most-hit screen.
- **Toggle** (`:45`) — the completion is added in `FetchAndPrepare`, and the base's `HandleAsync`
  (`BaseToggleIsDoneTodoListEndpoint.cs:25-52`) has **no try/catch either**, unlike the create/update/
  delete bases which all funnel through `DbUtils.HandleException`. Same unhandled 500.
- **Nightly job** (`:67-68`) — one batched `SaveChangesAsync` over every period's completions, so a
  single collision aborts the **whole remaining sweep**: no later period resets that night, and the
  grace-expiry mutations accumulated in the same change tracker are lost with it.
  `DisallowConcurrent = true` on the registration guards job-vs-job, not job-vs-request.

**Fix.** Catch `PostgresErrorCodes.UniqueViolation` around the completion insert and treat it as
"someone else already closed this period" — the mutation is idempotent by construction, so swallowing
is correct here. There are four in-repo precedents for the exact shape:
`framework/Sydowwe.Notifications/application/endpoint/quietHours/SetNotificationQuietHoursEndpoint.cs:65`,
`.../pushSubscription/SubscribePushEndpoint.cs:53`,
`framework/Sydowwe.Reminders/application/endpoint/preference/UpdateReminderKindPreferenceEndpoint.cs:51`,
and `RetryDbConcurrencyHelper.IsUniqueViolation` (`:108`) for the retrying variant. Additionally, doing
the job's inserts per-period rather than one batched `SaveChanges` stops one collision taking the sweep
down — that part is worth doing on its own regardless.

`Doc impact: none`

### `SEC-4` — **withdrawn.** See Corrections to the first pass.

### `SEC-5` 🟡 Concurrent first-time `PUT /routine/settings` 500s

`application/endpoint/routineSettings/UpdateRoutineSettingsEndpoint.cs:36-43`

Two concurrent first writes both find no row and both insert against the unique `user_id` index
(`UserRoutineSettingsConfiguration.cs:13`, via `IsOneWithOneUser`). One request gets an unhandled
`DbUpdateException` — the endpoint overrides `HandleAsync` with no try/catch. Low likelihood (it is one
dismissal click), listed because the fix is three lines and the framework already ships the shape at
`UpdateReminderKindPreferenceEndpoint.cs:51`, which is the same "upsert a single per-user settings row"
endpoint one module over.

`Doc impact: none`

---

## 6. Code quality

### `CQ-1` 🔴 Monthly and yearly anchored periods never reset — and every user has two

`domain/service/RoutineResetService.cs:36-76`

Both calendar-aligned branches end in a catch-up loop:

```csharp
var candidate = MonthlyCandidate(year, month, targetDay);
while (candidate < now) { month++; …; candidate = MonthlyCandidate(year, month, targetDay); }
return candidate;
```

The loop's postcondition is `candidate >= now`, unconditionally — if the initial candidate is already
`>= now` the loop simply doesn't run, and the return is still `>= now`. `MonthlyCandidate` always
returns **midnight UTC** (`:97-101`). Every caller then asks `if (now < nextReset) return null;`
(`:173`, `:197`). So a reset can only fire when `now` is midnight to the tick — and the nightly job runs
at **02:00** (`RoutinesScheduledJobsRegistrar.cs:28`), by which point today's candidate has already been
skipped to next month.

**A `LengthInDays` of 30 or 365 with a non-zero `ResetAnchorDay` therefore never resets, ever.** The
weekly-aligned branch (`:20-28`) is fine: it returns `earliest + daysUntil` derived from `LastResetAt`
and never consults `now`. So is the generic day-of-month branch (`:78-92`), for the same reason.

**Why it matters, and why nobody noticed.** `RoutineTimePeriodSeeder.Defaults`
(`infrastructure/persistence/seeder/userDefault/RoutineTimePeriodSeeder.cs:21-22`) gives **every user**
a "Monthly" (30d, anchor 1) and a "Yearly" (365d, anchor 1) out of the box. For those periods: items
never un-tick, `LastResetAt` stays null forever, no `RoutinePeriodCompletion` row is ever written, the
streak is never evaluated, and the completion heatmap stays permanently empty. It is silent in every
direction — `NextResetAt` projects a perfectly plausible future date (that *is* the loop's return
value), and the nudge path still fires, because `EvaluateEndingSoon` only needs
`now >= nextReset - lead && now < nextReset` (`:128`) and the loop keeps `nextReset` a month ahead. The
user gets a monthly "your period is ending soon" about a period that never ends.

**Fix.** The loop is trying to solve "the routine went dormant for several cycles", but it advances
against `now` instead of against `lastReset` — conflating "find the next boundary" with "find a boundary
in the future". Advance from `lastReset` until the candidate is strictly after `lastReset`, and let the
caller's `now < nextReset` comparison decide whether it is due. That is exactly what the weekly branch
does. Note this makes catch-up one cycle per invocation, same as the weekly path — see `CQ-8`; the two
want fixing together. Pair with `TEST-1`, whose absence is what let this ship.

`Doc impact: domain-map.md → Domain services (ComputeNextReset row: state that the returned instant is derived from LastResetAt and may be in the past — being in the past is what makes a period due)`

### `CQ-2` 🟠 Activity merge throws when two routines collide in the same period

`application/seam/RoutineTodoListActivityReferenceSource.cs:35-45` ·
`infrastructure/persistence/configuration/RoutineTodoListConfiguration.cs:53-54`

`RepointAsync` rewrites `ActivityId` on every routine referencing a merged activity, with no collision
handling. `RoutineTodoList` carries a unique index on `(UserId, TimePeriodId, ActivityId)`. If a user
has a routine on activity A and another on activity B **in the same time period**, merging B into A sets
both rows to `ActivityId = A` and the index rejects the save.

The class's own remark asserts the opposite outcome — "Two routines on two duplicate activities stay two
routines pointing at one activity" (`:18-22`) — and gives a good reason (folding two streaks would have
to invent a rule). The reasoning is sound; it just is not what the schema permits in the same-period
case. Both sides are wrong, so this is also `DOC-3`.

**Why it matters.** Activity merge is user-facing and fans out across six slices in one transaction
(`ActivityMergeTests.cs:85` names them). A `DbUpdateException` here fails the entire merge, not just the
routine part. `ActivityMergeTests` seeds exactly one routine on the duplicate
(`ActivityMergeTests.cs:131-133`) and none on the survivor — one row short of the failing case
(`TEST-2`).

**Fix.** Decide the collision rule and implement it. Least surprising: when the survivor already has a
routine in that period, delete the duplicate's row and keep the survivor's — its streak is the one the
user has been looking at — and say so in the remark. Detect it by grouping the loaded rows on
`(UserId, TimePeriodId)` before assigning.

`Doc impact: domain-map.md → Invariants (add the merge collision rule); and fix the XML remark on RoutineTodoListActivityReferenceSource`

### `CQ-3` 🟠 `RoutineTimePeriodValidator` does not mirror two of its DB constraints

`application/validator/RoutineTimePeriodValidator.cs:14-15` (and no `HistoryDepth` rule anywhere in the file)

- `LengthInDays` has `GreaterThanOrEqualTo(1)` and no upper bound; the DB check is `1..365`
  (`RoutineTimePeriodConfiguration.cs:30-32`).
- `HistoryDepth` has **no rule at all**; the DB check is `1..100` (`:50-52`) and the request's own XML
  doc claims "1–100" (`RoutineTimePeriodRequest.cs:25`).

Every other constraint in that configuration has a matching FluentValidation rule, several with a
comment saying so explicitly (`:20-23`, `:30-37`) — these two were missed. Because `DbUtils` maps
`CheckViolation` to `ValidationError` (`DatabaseExceptionUtils.cs:30-31`) and thence to 400, the
user-visible symptom is mild: a 400 carrying `"Validation error during HandleAsync"` instead of a
field-keyed error the form can attach to an input. That is also precisely why the existing tests do not
catch it — see `TEST-4`.

**Fix.** `RuleFor(x => x.LengthInDays).InclusiveBetween(1, 365);` and
`RuleFor(x => x.HistoryDepth).InclusiveBetween(1, 100);`.

`Doc impact: none`

### `CQ-4` 🟠 Updating a routine item destroys and recreates all of its steps

`application/endpoint/routineTodoList/command/UpdateRoutineTodoListEndpoint.cs:20-27`

`AfterMapping` replaces `entity.Steps` wholesale with freshly constructed `TodoListStep` objects. It
carefully carries `IsDone` across by looking up the old step by `s.Id` (`:26`) — but assigns it to a
**new** object with no `Id`, so EF deletes every existing step row and inserts replacements with new
keys.

**Why it matters.** Step identity churns on every save of the parent, including an unrelated one — a
note edit re-keys every step. Any client holding step ids has them invalidated;
`BaseToggleStepIsDoneEndpoint` addresses steps by `{stepId:guid}` in the route, so a concurrent step
toggle against a just-updated item 404s. It also generates delete+insert traffic proportional to the
step count for a no-op edit.

**Fix.** Reconcile in place: update steps whose `Id` matches, add those without one, remove those absent
from the request. `UpdateRoutineTodoListValidator` already models steps as having optional ids
(`:75-81`), so the request shape supports it.

`Doc impact: none`

### `CQ-5` 🟠 The flat read endpoints serve a malformed `RoutineTimePeriodResponse`, and refresh nothing

`application/dto/response/RoutineTimePeriodResponse.cs:47-78` ·
`application/dto/response/RoutineToDoListResponse.cs:63-79` ·
`application/endpoint/routineTimePeriod/query/GetAllRoutineTimePeriodEndpoint.cs` ·
`application/endpoint/routineTimePeriod/query/GetByIdRoutineTimePeriodEndpoint.cs` ·
`application/endpoint/routineTodoList/query/{GetAll,GetById}RoutineTodoListEndpoint.cs`

`RoutineTimePeriodResponse.From` exists precisely so the grouped read and the freeze endpoint cannot
drift, and its own doc says so (`:39-46`). But `Projection` — the raw expression `From` wraps — is what
the framework bases call directly: `BaseGetAllEndpoint.HandleAsync` does
`TResponse.Projection(query).ToListAsync(ct)` and `BaseGetByIdEndpoint.HandleAsync` does
`TResponse.Projection(source).FirstOrDefaultAsync(ct)`. Three consequences, all silent:

1. Anything served through those bases gets `NextResetAt = default(DateTime)` (`0001-01-01`) and an
   empty `CompletionHistory`.
2. The nested period inside `RoutineTodoListResponse.Projection` (`:63-79`) omits `ReminderLeadDays`,
   `FreezesRemaining`, `FreezeBudget` and `FreezeBudgetResetsAt` entirely, so they serialize as `null`,
   `0`, `0`, `null`. `RoutineTimePeriodResponse` documents the contract the SPA relies on: freezes are
   "always a number once the server supports freezes — the client reads `null` as 'this server has no
   freeze feature'" (`:23-27`). Serving a hard `0` tells the client the user has **no freezes left**
   rather than that the feature is absent, so the affordance renders disabled instead of hidden.
3. These reads call neither `RoutineStreakFreezeService.RefreshFreezeBudget` nor
   `RoutineResetService.CheckGrace`, so they can serve a stale unrefilled freeze count *and* a `Streak`
   whose grace window has already lapsed. That breaks two documented rules at once — see `DOC-4` and
   `DOC-7`.

**Fix.** Both bases already have the hook: override `PostProcessItemsAsync` on `BaseGetAllEndpoint` and
`PostProcessAsync` on `BaseGetByIdEndpoint` and re-derive through `From(period, history, now)`. Note
`PostProcess*` receives the *response*, not the entity, so either re-read the periods in the override or
hand-write these two the way the grouped read already is. Separately, add the four missing fields to the
nested projection in `RoutineToDoListResponse`, or cut the nested period down to the identifying fields
the client actually uses there.

`Doc impact: domain-map.md → Invariants (9) — no doc edit needed once the code is fixed, which is the point of fixing the code`

### `CQ-6` 🟠 The grace warning is marked as sent even when it failed

`infrastructure/jobs/RoutinePeriodNudgeJobHandler.cs:71-76` ·
`application/service/RoutinePeriodNotificationService.cs:55-62,69-84`

The two notification paths in the sweep are asymmetric. `NotifyEndingSoonAsync` returns `bool` and the
sweep stamps `EndingSoonNotifiedFor` only when it comes back true (`:64-68`) — deliberate, documented
on the interface (`IRoutinePeriodNotificationService.cs:25-29`), and tested.
`NotifyGraceExpiringAsync` returns bare `Task`, and `SafeNotifyAsync` swallows every non-cancellation
exception (`RoutinePeriodNotificationService.cs:80-83`), so the sweep stamps
`GraceNotifiedFor = period.StreakGraceUntil` unconditionally at `:74` whether or not anything was
delivered.

**Why it matters.** `ShouldWarnGraceExpiring` is keyed on that mark
(`RoutineResetService.cs:141`), so a transient SignalR/SMTP failure permanently suppresses the warning
for that grace window. The window then lapses and `CheckGrace` (`:151-159`) breaks the streak with no
warning ever having reached the user — the exact outcome the notification exists to prevent, reached
silently.

**Fix.** Make `NotifyGraceExpiringAsync` return `Task<bool>` like its sibling and gate the marker on it.
The interface doc already promises best-effort semantics for both; this only makes "was it actually
raised" observable for the second one.

`Doc impact: none`

### `CQ-7` 🟠 Item streak inflates on repeated toggles within one period

`domain/service/RoutineResetService.cs:258-267`

`UpdateItemStreak` increments unconditionally whenever the item is done, and is called from both toggle
endpoints (`RoutineToggleIsDoneTodoListEndpoint.cs:57`,
`ToggleStepIsDoneRoutineTodoListEndpoint.cs:37`). Nothing keys the increment to the period or to the
cycle. Two ways in:

- **Un-tick then re-tick** an ordinary item in the same cycle: `Streak` and `BestStreak` climb each time.
- **A counted item is worse.** `BaseToggleIsDoneTodoListEndpoint.IsDoneLogic` with `forceValue: true`
  clamps `DoneCount` to `TotalCount` and leaves `IsDone` true (`:91-101`), but the base still calls
  `AfterItemToggled` for every request (`:40`). So repeatedly sending `ForceValue = true` on an already
  complete counted item increments the streak every call, with no un-tick needed at all.

The period-level streak is not affected — it advances once per reset in the list `TryReset` (`:209`).
Only the per-item counters inflate.

**Fix.** Guard on the cycle: increment only if the item has not already been counted for the current
one (`item.LastCompletedAt < period.LastResetAt`, or compare `DateOnly.FromDateTime(item.LastCompletedAt)`
against `item.LastResetDate`). The entity carries both fields already
(`RoutineTodoList.cs:9,12`). `UpdateItemStreak` currently takes only the item, so it needs the period —
or the caller needs to make the decision.

`Doc impact: domain-map.md → Domain services (UpdateItemStreak row: note it is once-per-cycle, not once-per-toggle)`

### `CQ-8` 🟠 A dormant period catches up one cycle per invocation

`domain/service/RoutineResetService.cs:236`

`TryReset` sets `period.LastResetAt = nextReset` — the *next* boundary after the last one, not the
current time. With a daily nightly job, a period untouched for sixty cycles needs sixty job runs to
become current, writing one backdated `RoutinePeriodCompletion` per run and re-evaluating the streak
each time from items that have been sitting unticked throughout.

**Why it matters.** For a daily routine, "I was away for two months" produces two months of retroactive
misses arriving one per day, each firing an end-of-period notification
(`RoutineTodoListResetJobHandler.cs:74-75`) — a drip of "you missed Monday" for weeks. The grouped read
and the toggle hit the same path, so opening the app once resolves exactly one cycle. Separate from
`CQ-1`, but coupled to it: fixing `CQ-1` converts monthly/yearly periods from *never* resetting to
resetting on this same drip schedule, and for a 30-day period that drip is one cycle per *day*, so a
year-dormant monthly period would take twelve days to catch up while emitting twelve notifications.

**Fix.** Loop `TryReset` per period until it returns null, bounded — cap at `HistoryDepth` iterations
and jump `LastResetAt` forward past the remainder — and collapse the skipped cycles into a single
notification. The completion rows for skipped cycles are arguably worth writing (the heatmap should show
the gap); the notifications are not.

`Doc impact: domain-map.md → Domain services (TryReset list overload: document the catch-up semantics)`

### `CQ-13` 🟠 Spending a freeze does not check grace first, so a lapsed window can zero the streak the freeze just bought

`application/endpoint/routineTimePeriod/command/SpendStreakFreezeRoutineTimePeriodEndpoint.cs:48-98` ·
`domain/service/RoutineStreakFreezeService.cs:99-107`

`domain-map.md`'s business rules say `CheckGrace` "must run before `TryReset` on *every* access". The
freeze endpoint never calls it. `Apply` clears `StreakGraceUntil` / `GraceNotifiedFor` **only when the
frozen entry is the newest one** (`:101-105`) — correct and deliberate, because only that entry can own
the open window.

But if the window has already **lapsed** (`now > StreakGraceUntil`) and no read or job has cleared it
yet, and the user freezes an *older* miss, then: `Apply` leaves `StreakGraceUntil` set; `RecomputeStreak`
raises `Streak` back to the trailing run (`:148-149`, only ever raises); the response shows the restored
streak; and the very next grouped read or nightly sweep calls `CheckGrace`, sees the still-set lapsed
window, and sets `Streak = 0` (`RoutineResetService.cs:156`). The user spends a budgeted resource, sees
it work, and loses the streak on the next page load.

The race window is narrow — the client normally loads the grouped read first, which clears the lapsed
window — but it is real, and the failure is exactly the one the whole feature exists to prevent.

**Fix.** One line: `RoutineResetService.CheckGrace(period, now);` before the `Apply` call at `:90`.
Ordering is right — `CheckGrace` zeroes `Streak`, then `RecomputeStreak` raises it back from the
history including the newly frozen entry.

`Doc impact: domain-map.md → Business rules (Grace) — restate the rule as "before TryReset **and before Apply**", i.e. on every access, which is what it already says and what the code will then do`

### `CQ-9` 🟡 `RoutinePeriodCompletion` is the one routines table outside the entity-base convention — and it shows in the schema

`infrastructure/persistence/configuration/RoutinePeriodCompletionConfiguration.cs:10` ·
`domain/model/entity/RoutinePeriodCompletion.cs:5,13`

The configuration hand-rolls `builder.HasKey(x => x.Id)` with no `BaseEntityConfigure()` call, and the
entity declares its own `CreatedAt` (`:13`). CLAUDE.md is explicit that configurations "always use the
builder extension helpers — call `BaseEntityConfigure()` first, and never hand-roll `ToTable` / `HasKey`
/ row_version / timestamps." The other three configurations in this module comply.

This is **not** a one-line fix, and the first pass got that wrong.
`EntityBuilderExtensions.BaseEntityConfigure<TEntity>` is constrained
`where TEntity : class, IBaseTableEntity`, which `BaseEntity` does not satisfy — only `BaseTableEntity`
does. Calling it on `RoutinePeriodCompletion` will not compile until the entity changes base class.

The divergence is visible in the model snapshot
(`AdhdTimeOrganizer/infrastructure/persistence/Migrations/AppDbContextModelSnapshot.cs:2219-2228`):

- **table name `routine_period_completions` — plural.** `BaseEntityConfigure` derives the table from the
  *class* name, giving the singular `routine_todo_list` / `routine_time_period` /
  `user_routine_settings` everywhere else; without it EF falls back to the DbSet name via
  `UseSnakeCaseNamingConvention`. `ModuleSchemaTests.cs:61` pins the plural form, so this is load-bearing
  today, not cosmetic.
- **no `row_version`** — the only routines table without a concurrency token.
- **`UseIdentityByDefaultColumn` instead of `UseSerialColumn`** — every other table uses serial, from
  `EntityBuilderExtensions.cs:20`.

**Fix (if taken).** Promote `RoutinePeriodCompletion` to `BaseTableEntity`, drop the redundant
`CreatedAt`, call `BaseEntityConfigure()`. That is a **table rename + column add + column drop**
migration plus a `ModuleSchemaTests` edit, not a no-op — see `MIG-3`. Leaving it as-is is defensible;
what is not defensible is the current state being undocumented, since the next person to add a
convention to `BaseEntityConfigure` will silently miss this table.

`Doc impact: domain-map.md → Persistence (RoutinePeriodCompletionConfiguration row: say it does not go through BaseEntityConfigure, and that the table is consequently plural and has no row_version)`

### `CQ-10` 🟡 Two dead filter DTOs, both exposing a settable `UserId`

`application/dto/filter/RoutineTimePeriodFilterRequest.cs` ·
`application/dto/filter/RoutineToDoListFilterRequest.cs`

Neither type is referenced anywhere in the solution — a whole-repo search returns only the two
declarations, `review/portal/REMAINING.txt` and this review. The module has no grid or filter endpoint.
Both records expose `public long? UserId { get; set; }` — harmless while unused, but it is a
mass-assignment shape waiting for someone to wire a `BaseGridEndpoint` against it and hand a caller a
`userId` filter parameter.

**Fix.** Delete both. If a routine grid is planned, reintroduce them without `UserId` — scoping is the
DbContext's job here (invariant 3).

`Doc impact: domain-map.md → the DTO paragraph says "two filters in dto/filter/"; drop that clause when the files go`

### `CQ-11` 🟡 Both to-do list validators open a DI scope per validation, and duplicate 30 lines verbatim

`application/validator/CreateRoutineTodoListValidator.cs:34-63` ·
`application/validator/UpdateRoutineTodoListValidator.cs:44-73`

Both take `IServiceScopeFactory` and build a fresh `AsyncScope` to resolve a `DbContext` for one scalar
lookup. FastEndpoints validators are resolved from the request scope; injecting `DbContext` directly is
simpler, shares the request's transaction, and — relevant to `SEC-1` — keeps the ambient user context
rather than resolving a context whose `ScopeUserId` provenance is a fresh scope's business. The 30-line
`SuggestedDays` / `SuggestedDayOfMonth` bucket rule is then duplicated character-for-character between
the two files, so a change to the bucket boundaries has to land twice.

**Fix.** Inject `DbContext`; extract the bucket rule into a shared private static helper or a small
`AbstractValidator` base the two derive from. Do this together with `SEC-1` — same two methods.

`Doc impact: none`

### `CQ-12` 🟡 Three different user-scoping styles across the endpoints

The module mixes: explicit `x.UserId == loggedUserId` predicates
(`GetAllGroupedRoutineTodoListEndpoint.cs:32`, `SpendStreakFreezeRoutineTimePeriodEndpoint.cs:57`,
`GetCompletionHistoryRoutineTimePeriodEndpoint.cs:31`, and all three step endpoints' `GetParentQuery`
overrides); documented reliance on the global filter, with an `AuthorizeAsync` override returning `true`
and a one-line comment (`GetByIdRoutineTodoListEndpoint.cs`, `GetByIdRoutineTimePeriodEndpoint.cs`); and
silent reliance with no comment (`RoutineToggleIsDoneTodoListEndpoint.cs:24-37`,
`ToggleStepIsDoneRoutineTodoListEndpoint.cs:14-21`).

All three are correct today and invariant 3 sanctions the filter-only form. The problem is that a reader
cannot tell "deliberately relying on the filter" from "forgot" — which is exactly how `SEC-1` reads at a
glance. The freeze endpoint's comment (`:54-55`) is the right model.

**Fix.** Comment the filter-only sites, or standardise on the explicit predicate for anything that takes
an id from the request.

`Doc impact: none`

### `CQ-14` 🟡 A freeze can be spent on a period that had no items

`domain/service/RoutineStreakFreezeService.cs:65-67,88`

`IsSuccess` returns false when `TotalCount == 0` (`:66`), so `Apply`'s `NotAMiss` guard (`:88`) lets a
freeze through on an item-less period. But `RecomputeStreak` already skips those rows unconditionally
(`:133`) — an empty period carries the run for free, and `TryReset` leaves the streak untouched
(`RoutineResetService.cs:201-202`, outcome `NotEvaluated`). So the freeze is charged against the budget
and changes nothing. The client should not offer it, but the client is not the guard here — the
endpoint's whole design is that the domain rules live in `StreakFreezeRejection` because they are "not
knowable from the request body" (`StreakFreezeRejection.cs:5-9`).

**Fix.** Treat `TotalCount == 0` as `NotAMiss`: `if (entry.TotalCount == 0 || IsSuccess(entry, …))`. The
enum member's summary ("there is no miss to cover") already describes the right answer.

`Doc impact: none`

### `CQ-15` 🟡 An item can be moved to another period without re-deriving its display order

`application/dto/request/UpdateRoutineTodoListRequest.cs:22`

`UpdateEntity` reassigns `TimePeriodId`, but `DisplayOrder` is scoped per period —
`ChangeDisplayOrderRoutineTodoListEndpoint.GroupFilterExpression => e.TimePeriodId` (`:11`), and
`CreateRoutineTodoListEndpoint` derives it per period on insert via
`RoutineTodoListExtensions.GetNextDisplayOrder(…, entity.TimePeriodId, …)` (`:27`). A `PUT` that changes
the period keeps the old order value, so the item lands at an arbitrary position in the destination and
can tie with an existing one. (The unique index `(UserId, TimePeriodId, ActivityId)` still protects
against a duplicate activity, and `BaseUpdateEndpoint` maps that to a clean 409.)

**Fix.** In `AfterMapping`, when `TimePeriodId` changed, re-derive `DisplayOrder` through the same
extension the create path uses. `BaseUpdateEndpoint` exposes `DbContext` and loads the row tracked, so
the original value is readable off the entry — that is what the property is documented for
(`BaseUpdateEndpoint.cs`, "so a subclass hook can read tracked/original values").

`Doc impact: none`

---

## 9. Performance hotspots

| Where | Issue | Impact | Mitigation | ID |
|---|---|---|---|---|
| `GetAllGroupedRoutineTodoListEndpoint.cs:76-91` | Loads the **entire** completion history for every period, then trims to `HistoryDepth` in memory (`:89`) | History grows one row per period per cycle forever; a two-year-old daily routine is ~730 rows fetched to render 16. Grows unboundedly with account age, on the module's most-hit read | `PARTITION BY … ORDER BY period_start DESC` in SQL, or a per-period `Take(HistoryDepth)` — the pattern `GetCompletionHistoryRoutineTimePeriodEndpoint.cs:42-47` already uses correctly | `PERF-1` |
| `GetAllGroupedRoutineTodoListEndpoint.cs:102` | `RoutineTodoListResponse.Projection(new[] { e }.AsQueryable()).Single()` — **per item** | Builds and interprets the whole projection expression tree once per item via LINQ-to-Objects, on a request that already holds the materialised graph | Project the list once outside the loop, or map with a plain function. The expression is only needed for the `IQueryable` path | `PERF-2` |
| `RoutineTodoListResetJobHandler.cs:34-37` | Loads every user's periods with `RoutineTodoListColl` + `Steps` into memory in one go | Whole-table materialisation with two levels of collection include; memory scales with total users × routines × steps. Also makes the single `SaveChangesAsync` at `:68` an all-or-nothing batch — see `SEC-3` | Page by user id and commit per page | `PERF-3` |
| `RoutinePeriodNudgeJobHandler.cs:40-43` | Same shape, plus `Where(p => !p.IsHidden)` with no supporting index | Sequential scan of `routine_time_period` daily. Small today; the sequential-notify loop below it is deliberate and documented (`:49-53`), so this is the only part worth changing | Page by user; see index note below | `PERF-4` |
| `RoutineStreakFreezeService.RecomputeStreak` via `SpendStreakFreezeRoutineTimePeriodEndpoint.cs:77-80` | Loads the complete history to recompute | **Deliberate and correct** — a truncated history under-counts the run, and the code says so at `:75-76`. Listed only so it is not "found" again later; its growth is bounded by fixing `PERF-1`, not this | none | — |

### Indexes that should exist

- `routine_period_completion (time_period_id, period_start DESC)` — the unique index on
  `(time_period_id, period_start)` already serves ordered lookups by period, so this is only worth
  adding if `PERF-1` is fixed with a windowed query that wants the descending order. **Probably no
  action.**
- `routine_time_period (is_hidden)`, or a partial index `WHERE NOT is_hidden` — supports the nudge
  sweep's filter (`PERF-4`). Marginal at current scale; add with `PERF-3`'s paging, not before.
- **No missing index on the hot read path.** `routine_todo_list` already carries
  `(user_id, time_period_id)` as an explicit performance index
  (`RoutineTodoListConfiguration.cs:57`) and the unique `(user_id, time_period_id, activity_id)` above
  it (`:53-54`). The grouped read's cost is `PERF-1` and `PERF-2`, not index coverage — worth stating
  plainly so nobody "fixes" it with an index.

---

## Code/doc drift

### `DOC-1` — **resolved since the first pass.** The docs' folder paths are now correct, and `domain-map.md` carries an explicit note that the namespaces still contain a `todoList` segment the folders do not. Nothing to fix.

### `DOC-2` — **resolved since the first pass.** `summary.md` now says "the 23 routine endpoints", which matches the tree (2 settings + 5 time-period commands + 4 time-period queries + 6 to-do commands + 3 to-do queries + 3 step endpoints).

### `DOC-3` 🟠 The merge remark asserts behaviour the schema forbids

`application/seam/RoutineTodoListActivityReferenceSource.cs:18-22` — see `CQ-2`. Both the code and the
comment need changing; the comment's *reasoning* (don't invent a streak-folding rule) should survive
into whatever collision rule is chosen.

`Doc impact: domain-map.md → Invariants (new merge-collision rule)`

### `DOC-4` 🟠 Invariant 9 does not hold — there is a fourth surface that shows the freeze budget

`domain-map.md` → Invariants, row 9 ("The freeze budget is refilled by whoever reads it, not by a job")
and `summary.md`'s freeze gotcha, which names the three sites that do call `RefreshFreezeBudget`: the
grouped read, the nightly reset job, and `Apply` itself.

`GET /routine-time-period` and `GET /routine-time-period/{id}` also show `FreezesRemaining` /
`FreezeBudget` / `FreezeBudgetResetsAt` — they run `RoutineTimePeriodResponse.Projection`, which selects
all three (`:74-76`) — and refresh nothing. So does anything reading the nested period on
`GET /routine-todo-list` / `{id}`, except that one serves a hard `0` instead (`CQ-5`). The invariant is
the right rule; the code is what is wrong.

`Doc impact: domain-map.md → Invariants (9) — no edit once CQ-5 is fixed, which is the argument for fixing the code rather than the doc`

### `DOC-5` 🟡 A test name contradicts the documented `TryReset` contract

`AdhdTimeOrganizer.IntegrationTests/Services/RoutineResetServiceTests.cs:204` —
`TryReset_Single_AfterResetTime_ResetsItemAndUpdatesPeriod`. The single-item overload explicitly does
not update the period (`RoutineResetService.cs:161-169`), and `domain-map.md`'s Domain services table
restates that. The name asserts the opposite, and the body's `period.LastResetAt.Should().NotBeNull()`
(`:218`) is vacuous — the fixture built the period with `lastResetAt: UtcNow.AddDays(-8)`, so it would
pass if the method were deleted. See `TEST-10`.

Rename to `..._ResetsItemWithoutAdvancingLastResetAt` and assert `LastResetAt` is **unchanged**, which is
the actual guarantee and is currently unpinned.

`Doc impact: none (fix the test)`

### `DOC-6` 🟡 The docs say completions cascade on period delete, but omit that items block it

`domain-map.md` → Navigation index, `DeleteRoutineTimePeriodEndpoint` row: "Delete a period; completions
cascade". True (`RoutinePeriodCompletionConfiguration.cs:26-29`) but half the story:
`RoutineTimePeriod → RoutineTodoList` is `OnDelete(DeleteBehavior.Restrict)`, declared twice
(`RoutineTimePeriodConfiguration.cs:74-77` and `RoutineTodoListConfiguration.cs:47-50`), so deleting a
period that still has items is refused. `DbUtils` maps the FK violation to `DbForeignKeyError` →
**409** (`EndpointHelper.cs:14`), which is the right answer and is pinned by
`TodoListDeleteBehaviorTests.DeleteRoutineTimePeriod_WithItems_Returns409`. The behaviour is fine; the
doc row reads as though the delete always succeeds.

`Doc impact: domain-map.md → Navigation index (DeleteRoutineTimePeriodEndpoint row: "completions cascade, items Restrict — a period with items is a 409")`

### `DOC-7` 🟡 "CheckGrace before every access" is asserted as a rule and violated by three sites

`domain-map.md` → Business rules, **Grace**: "`CheckGrace` must run before `TryReset` on *every*
access — a lapsed window breaks the streak, and if only the reset job checked it, a streak would survive
indefinitely as long as nobody's period rolled over."

Three surfaces skip it: `GetAllRoutineTimePeriodEndpoint` and `GetByIdRoutineTimePeriodEndpoint` (they
serve `Streak` from a lapsed window — `CQ-5`), and `SpendStreakFreezeRoutineTimePeriodEndpoint`, where
skipping it is actively harmful (`CQ-13`). The rule is right; fix the code, and while doing so restate
the rule as "before `TryReset` **and before `Apply`**" so the freeze path is named rather than implied.

`Doc impact: domain-map.md → Business rules (Grace) — name Apply alongside TryReset`

---

## Corrections to the first pass

Recorded so the same conclusions are not re-derived. Four claims in the first-pass file were wrong.

| First-pass claim | Status | What is actually true |
|---|---|---|
| `SEC-4` — "check-constraint violations return the raw Postgres message to the client" | **Withdrawn** | `DbUtils.HandleException` puts `pgEx.Message` in the Result's *third* argument (`Result.Error(type, errorMessage, exceptionMessage)` — `Result.cs:44`). Every base endpoint then calls `AddError(result.ErrorMessage!)`, i.e. only the generic second argument (`"Validation error during HandleAsync"`). The Postgres text is never sent. |
| `CQ-9` fix — "Replace with `builder.BaseEntityConfigure();` … verify the generated migration is empty" | **Wrong on both counts** | `BaseEntityConfigure<TEntity>` is constrained `where TEntity : class, IBaseTableEntity`; `RoutinePeriodCompletion : BaseEntity` does not satisfy it, so the call does not compile. And the migration would not be empty — it is a table rename (`routine_period_completions` → `routine_period_completion`), a `row_version` add and a `created_at` change. `CQ-9` above is rewritten accordingly. |
| `DOC-1` — "every folder path in the docs is stale"; `DOC-2` — "endpoint count disagrees (21 vs 23)" | **Both already fixed** | The docs were rewritten after the first pass. Paths are correct, `domain-map.md` carries an explicit "trust the path column, not the namespace" note, and `summary.md` says 23. |
| `DOC-4` cited `domain-map.md:152-154` and "invariant 5" for the freeze-refresh rule | **Wrong anchor** | That rule is **invariant 9** in the current `domain-map.md`; invariant 5 is the `(TimePeriodId, PeriodStart)` uniqueness. Re-anchored above. The underlying finding stands. |
