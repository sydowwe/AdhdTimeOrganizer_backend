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

### `SEC-1` 🔴 ✅ FIXED 2026-09-06 · No ownership check on `TimePeriodId` — a routine item can be attached to another user's period

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

**Fixed 2026-09-06**, in the validators, together with `CQ-11` — same two methods, as the finding
prescribed. Both now delegate to `RoutineTodoListPeriodRules.ApplyAsync`, which resolves the period
with `p.Id == timePeriodId && p.UserId == userId` and adds a `TimePeriodId` failure when nothing comes
back, before the bucket rule runs. Same message for "does not exist" and "is not yours" — the
anti-oracle rule `LookupOwnership` and Tracking `SEC-2` already follow.

The user id comes off `IHttpContextAccessor`, not `ILoggedUserService`: FastEndpoints validators are
singletons, which is why these two build their own scope in the first place, and the accessor is what
the four ActivityProfiles validators with the same shape already take. Injecting the request-scoped
`DbContext` — the finding's other suggestion, and `CQ-11`'s — would be a captive dependency, so the
scope factory stays. The explicit `UserId` comparison is what decides, so the fresh scope's
`ScopeUserId` provenance no longer matters.

**No `AfterMapping` re-assert.** The belt-and-braces suggestion would be a second round-trip on every
write to re-check what the validator just rejected, and the risk it guards against — a mapping
refactor — cannot reach the validator. The invariant is documented and pinned instead.

Pinned on rows, not status codes, in
`AdhdTimeOrganizer.IntegrationTests/Endpoints/RoutineTodoListPeriodRulesTests.cs`:
`CreateRoutineTodoList_WithForeignTimePeriodId_IsRejected` (asserting nothing was written into the
victim's period) and `UpdateRoutineTodoList_WithForeignTimePeriodId_IsRejected` (asserting the row
still points at the caller's own period), plus
`CreateRoutineTodoList_WithOwnTimePeriodId_Succeeds` so the guard cannot degrade into a blanket
rejection. The bucket rule had no test at all before the extraction; two now cover it from both
validators.

`Doc impact: domain-map.md → Invariants (add: "TimePeriodId on a routine item is caller-supplied; neither the FK nor the global filter checks that the caller owns it — the validators must")` — done, as invariant 11, plus the validators paragraph and a guard row in testing.md.

### `SEC-2` 🟠 ✅ FIXED 2026-09-06 · `PUT /routine-todo-list/{id}` writes `IsDone` / `DoneCount` / `DisplayOrder` raw, skipping the toggle logic and the cross-slice event

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

**Fixed 2026-09-06**, the way the finding prescribed: `UpdateEntity` no longer assigns `IsDone`,
`DoneCount` or `DisplayOrder`, and says at the call site why — the toggle endpoint owns completion
because it is the path that reconciles the three representations of "done", advances the item streak and
publishes `RoutineTodoListIsDoneChangedEvent`.

The three properties were **not** removed from the DTO: they are inherited from
`BaseUpdateTodoListRequest`, which `UpdateTodoListItemRequest` also derives from — the same finding is
open against that one as the todo-lists review's `SEC-5`/`CQ-16`, and narrowing the shared base from
here would decide it for that slice too. They arrive and are ignored, as `RoutineTimePeriodRequest`
already does for a period's derived fields.

Two consequences worth recording, both in `AfterMapping`:

- `DoneCount` is now re-derived rather than dropped — `Math.Min(entity.DoneCount ?? 0, TotalCount)` when
  the item is counted. `TotalCount` stays client-owned, so lowering it under the count already recorded
  would otherwise hit `CK_RoutineTodoList_DoneCount_LessOrEqual_TotalCount`. `IsDone` is left alone
  rather than re-derived from the new ratio: re-deriving it would re-open a completed item without the
  event, which is the failure this finding is about, pointed the other way.
- The two `DoneCount` rules came out of `UpdateRoutineTodoListValidator`. Keeping them would 400 a
  client that echoes the item's current `DoneCount` back alongside a lowered `TotalCount` — the most
  ordinary edit there is — over a value the server now ignores.

Pinned in `AdhdTimeOrganizer.IntegrationTests/Endpoints/RoutineTodoListUpdateEndpointTests.cs`:
`UpdateRoutineTodoList_CannotWriteIsDoneOrDisplayOrder` (asserting on the row: `IsDone` false and
`DisplayOrder` unmoved after a `PUT` that asked for both, with `Note` written so the test cannot pass by
the endpoint refusing everything) and `UpdateRoutineTodoList_LoweringTotalCount_ClampsDoneCount`.

`Doc impact: domain-map.md → Business rules (state that IsDone/DoneCount/DisplayOrder on a routine item are server-owned, and why)` — done, as the "Completion is server-owned" paragraph, plus a guard row in testing.md.

### `SEC-3` 🟠 ✅ FIXED 2026-09-06 · Three reset sites race on the unique `(TimePeriodId, PeriodStart)` index, and none of the three handles the collision

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

**Fixed 2026-09-06, all three sites**, including the per-period `SaveChanges` in the job.

One correction to the finding, which widens the fix rather than changing it. A unique violation is only
**half** the race. `RoutineTimePeriod` carries a `row_version` concurrency token (every table through
`BaseEntityConfigure` does), and the reset updates the period as well as inserting the completion — so the
loser surfaces as a `DbUpdateConcurrencyException` whenever EF's batch happens to execute the period update
before the insert. Catching only `PostgresErrorCodes.UniqueViolation`, as the four cited precedents do,
would have left roughly half the collisions 500ing. Both shapes mean the same thing and are recognised
together in `infrastructure/persistence/RoutineResetConflict.cs`, whose remark records why — and warns that
it may only wrap a save whose entire change set *is* the reset, since an unrelated unique violation in the
same batch is indistinguishable from here.

Swallowing is correct for the reason the finding gives — every value the reset writes is derived from the
period and the clock alone, so the winner wrote exactly what the loser was about to — but *what to do with
the loser's uncommitted work* differs per site, and none of the three could simply ignore the exception:

- **Grouped read** — `ChangeTracker.Clear()` and carry on rendering from the in-memory periods, which hold
  the same values the winner committed. The `reset` list is cleared too: the winner owes the
  end-of-period summary, and two requests announcing one reset is the same bug pointed the other way.
- **Toggle** — the reset is now committed **on its own, ahead of the toggle**, in `FetchAndPrepare`. That is
  the change that makes recovery possible at all: the reset is the only part of the request that can lose
  the race, so on a collision the tracker is cleared and the periods re-read, and the user's tick then
  lands on the committed state instead of on a change set the database has already rejected. Splitting the
  transaction is deliberate — the reset is idempotent and independent of whether the toggle succeeds.
- **Nightly job** — one `SaveChangesAsync` per period, as the finding asked. The recovery here cannot use
  `ChangeTracker.Clear()`: it would detach the periods not yet visited and their later saves would silently
  write nothing. `DiscardAsync` instead detaches just the added completions and reloads that period and its
  items, so the rest of the sweep proceeds untouched. A skipped period logs at Information and is picked up
  by the next access.

Pinned in `AdhdTimeOrganizer.IntegrationTests/Routines/RoutineResetConcurrencyTests.cs` (4 cases for this
finding). The loser is **simulated, not raced**: the test seeds the completion row the winner's transaction
would have written for exactly the cycle the site is about to close — the one row that can collide — so
every case is deterministic where a real race would be timing-dependent. `GroupedRead_WithNoCollision_…` is
half the pin, because "catch and carry on" could otherwise degrade into "never write anything" and the
other cases would still pass. The sweep case asserts on the **other** period's rows, which is the part the
batched save used to lose.

`Doc impact: none` — a guard row was added to `testing.md` anyway, since this is exactly the kind of
mechanism that breaks without an error.

### `SEC-4` — **withdrawn.** See Corrections to the first pass.

### `SEC-5` 🟡 ✅ FIXED 2026-09-06 · Concurrent first-time `PUT /routine/settings` 500s

`application/endpoint/routineSettings/UpdateRoutineSettingsEndpoint.cs:36-43`

Two concurrent first writes both find no row and both insert against the unique `user_id` index
(`UserRoutineSettingsConfiguration.cs:13`, via `IsOneWithOneUser`). One request gets an unhandled
`DbUpdateException` — the endpoint overrides `HandleAsync` with no try/catch. Low likelihood (it is one
dismissal click), listed because the fix is three lines and the framework already ships the shape at
`UpdateReminderKindPreferenceEndpoint.cs:51`, which is the same "upsert a single per-user settings row"
endpoint one module over.

**Fixed 2026-09-06**, as the finding prescribed and in the precedent's exact shape: the insert branch gets
its own `SaveChangesAsync`, and a `23505` on `ix_user_routine_settings_user_id` clears the tracker, re-reads
the winning row and updates it. The `SaveChangesAsync` moved into both branches, since only one of them
needs wrapping. Unlike `SEC-3` this catches the unique violation **only** — there is no concurrency-token
half here, because the loser of a first-write race updates a row it has just read fresh.

Pinned by `RoutineResetConcurrencyTests.FirstTimeSettingsWrite_UnderConcurrency_AllSucceed_AndLeaveOneRow`,
which races two writes on an account with no row and asserts both `204`s and a single row. This one is
genuinely raced rather than simulated: the recovery branch needs the row absent at read time and present at
insert time, which nothing can arrange from outside. Two writers is both the scenario the finding describes
and the shape the recovery is written for; in the rare run where they do not overlap the test passes
vacuously rather than flaking.

**One residual, deliberately left.** Two concurrent writes to a settings row that *already exists* still
collide on `row_version` and 500 — the `else` branch, untouched by this fix, and the same for all four
framework precedents. A five-way version of the test above surfaced it (the losers all recover, then contend
with each other on the update). It is a different finding from this one, which is about the first write, and
fixing it means either a bounded retry loop or an `ExecuteUpdate`-based upsert — a shape no endpoint in the
solution uses today. Worth raising on its own rather than smuggling in here.

`Doc impact: none`

---

## 6. Code quality

### `CQ-1` 🔴 ✅ FIXED 2026-09-06 · Monthly and yearly anchored periods never reset — and every user has two

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

**Fixed 2026-09-06**, together with `CQ-8` — the two were coupled exactly as the finding said. Both
`while (candidate < now)` loops are gone; the monthly and yearly branches now return the first
candidate after `lastReset` and nothing else, which is what the weekly branch always did. The returned
instant is therefore allowed to be in the past, and `ComputeNextReset`'s `now` argument no longer moves
the answer at all — it is kept only for symmetry with the rest of the service, and the XML doc says so.

The catch-up the loops were reaching for lives in `TryResetCatchUp` (`CQ-8`), where it can advance
`LastResetAt` and write the completion rows rather than silently skipping cycles.

Pinned by `ComputeNextReset_MonthlyPeriod_WhenTheBoundaryHasPassed_ReturnsThatBoundary_NotTheNextOne`
and its yearly twin, `TryReset_Batch_MonthlyPeriodPastItsAnchor_Resets` (the assertion that matters —
`ComputeNextReset` is only interesting through its callers), the negative control
`TryReset_Batch_MonthlyPeriodBeforeItsAnchor_DoesNotReset` so the fix cannot degrade into "always
return a past boundary", and at sweep level by
`ResetSweep_MonthlyAnchoredPeriod_ResetsWhenItsAnchorHasPassed`. That last one needed
`SeedPeriodAsync` to stop hard-coding `ResetAnchorDay = 0`, which is why every existing test in the
file only ever exercised the branch that worked.

`Doc impact: done — domain-map.md's ComputeNextReset row and the Reset-and-streak-scoring rule both state that the instant is derived from LastResetAt and may be in the past`

### `CQ-2` 🟠 ✅ FIXED 2026-09-07 · Activity merge throws when two routines collide in the same period

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

**Fixed 2026-09-07**, with the rule the finding proposed and in the shape
`ActivityProfileActivityReferenceSource.RepointUniqueProfileAsync` already uses one slice over — the
merge's other collision rule, and the reason `RepointAsync` is a seam method the owning slice implements
rather than a generic FK rewrite. `RepointAsync` loads the merged rows ordered by id, groups them on
`(UserId, TimePeriodId)`, and per group keeps the survivor's routine if it has one and deletes the rest.

One addition the finding did not spell out: **where the survivor has no routine in that period**, the
oldest merged row is repointed and survives rather than everything being dropped — folding a
long-running duplicate into a survivor that never had the habit should keep the streak. Ordered by id so
that with several merged activities in one period the answer is "the habit kept longest", not whichever
row Postgres returned first. Deleted rows still count as repointed, per the seam's own remark.

Nothing else is lost with the deleted row: its steps are owned `ToJson` (they live in the row), and
`ActivityHistory.RoutineTodoListId` is `SetNull` while the history's own `ActivityId` is repointed by
History's source in the same transaction.

Pinned in `ActivityMergeTests`: `Merge_TwoRoutinesInTheSamePeriod_ResolvesTheCollision` (asserting the
surviving row's `Streak` is the survivor's 7, not just that one row survived — keeping the *wrong* one
looks identical to a count assertion) and `Merge_TwoRoutinesInDifferentPeriods_KeepsBothAndRepointsBoth`
as the control, which is the case the seam's remark describes and which the collision rule must not
swallow. `SeedRoutineTodoListAsync` gained a `streak` parameter so the two rows are distinguishable.

`Doc impact: domain-map.md → Invariants (add the merge collision rule); and fix the XML remark on RoutineTodoListActivityReferenceSource` — done, as invariants 12 (the unique index it rests on) and 13, plus the seam row in the navigation index, `summary.md`'s seam bullet, and the remark itself (`DOC-3`).

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

### `CQ-4` 🟠 ✅ FIXED 2026-09-06 · Updating a routine item destroys and recreates all of its steps

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

**Fixed 2026-09-06**, exactly that: `ReconcileSteps` in the endpoint mutates the matched step and keeps
the instance, so the `Guid` and `IsDone` survive; a request step without an id is new; one the request
omits is gone.

One correction to the finding's reasoning, which does not change the fix. Steps are an owned collection
serialised **`ToJson`** into the item's row (`TodoListEntityConfigurationExtensions.cs`, `OwnsMany` +
`ToJson`), so there are no step rows and there was never delete+insert traffic — the whole document is
rewritten either way. What was real is the identity loss, and that is the part that hurts: `Id` defaults
to `Guid.NewGuid()` and is `ValueGeneratedNever()`, so every save minted fresh ids and the three routes
taking `{stepId:guid}` in the path 404 against steps still on screen.

Pinned in `RoutineTodoListUpdateEndpointTests`: `UpdateRoutineTodoList_ReconcilesStepsInPlace` (id and
tick survive, name/note overwritten, an id-less step gets a fresh id, an omitted one is dropped) and
`UpdateRoutineTodoList_WithoutSteps_LeavesThemAlone` for the null-`Steps` path.

The identical bug in `UpdateTodoListItemEndpoint` was left alone — it is the todo-lists review's own
`CQ-19`, with a written testing prompt, and it is one slice over.

`Doc impact: none`

### `CQ-5` 🟠 ✅ FIXED 2026-09-06 · The flat read endpoints serve a malformed `RoutineTimePeriodResponse`, and refresh nothing

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

**Fixed 2026-09-06, all three consequences, on all four endpoints.** The re-derivation lives once, in
`application/service/RoutineTimePeriodReadRefresher.cs`: it loads the periods tracked, runs `CheckGrace`
and `RefreshFreezeBudget`, **persists** them (projecting alone would hand out a fresh budget on every
read and re-break the same streak), then renders through the shared `From(period, history, now)` with
the history trimmed to `HistoryDepth` and handed back oldest-first.

The four endpoints keep their framework bases and call it from the hooks the finding named —
`PostProcessItemsAsync` on the two `BaseGetAllEndpoint`s, `PostProcessAsync` on the two
`BaseGetByIdEndpoint`s. Keeping the bases costs a second read of rows the projection already touched;
that buys the route, the roles, `Sort` and the 404 semantics, and these are not the hot path (the
grouped read is). `PostProcess*` receives the response, not the entity, exactly as the finding warned,
which is why the helper re-reads by id. The two to-do-list reads swap only the **nested** period, so
the item's own projected fields are untouched. Order is preserved by mapping over the base's list
rather than over the helper's dictionary.

Point 2 is fixed at the projection as well, not just papered over by the hook: `ReminderLeadDays`,
`FreezesRemaining`, `FreezeBudget` and `FreezeBudgetResetsAt` are now selected in
`RoutineToDoListResponse`'s nested period. Cutting the nested period down to identifying fields — the
finding's other option — would have changed the wire shape for a SPA that is not in this repo, so the
non-breaking half was taken. `NextResetAt` / `CompletionHistory` stay out of the projection with a
comment saying who is expected to fill them, because neither is computable in SQL.

Deliberately **no reset** in the helper. A reset scores the cycle from the period's items, and a flat
read never loads them; resetting there would write a completion row from a list it never saw. The three
reset sites are unchanged.

Pinned in `AdhdTimeOrganizer.IntegrationTests/Endpoints/RoutineFlatPeriodReadTests.cs` (7 cases): the
two derived members on both period reads, the budget refill and the grace break asserted **on rows as
well as on the response**, the nested period on `GET /routine-todo-list`, and — the other half of the
pin — that an *open* budget window and an *open* grace window are left alone, since an over-eager
refresh makes the budget infinite and grace worthless.

`Doc impact: domain-map.md → Invariants (9) — no doc edit needed once the code is fixed, which is the point of fixing the code` — the invariant now also names the surfaces, and the Grace rule, the two navigation rows and `summary.md`'s freeze gotcha name the helper. See `DOC-4` / `DOC-7`.

### `CQ-6` 🟠 ✅ FIXED 2026-09-07 · The grace warning is marked as sent even when it failed

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

**Fixed 2026-09-07** as prescribed. `SafeNotifyAsync` now returns `bool` — true on dispatch, false on
the swallowed-and-logged branch — so "was it raised" is answered by the one place that knows.
`NotifyGraceExpiringAsync` returns `Task<bool>` off it and the sweep gates both the marker and the
counter on the result (`RoutinePeriodNudgeJobHandler.cs:82-90`).

One thing beyond the finding: `NotifyEndingSoonAsync` now returns that same value instead of a bare
`true`, so a *failed* nudge no longer stamps `EndingSoonNotifiedFor` either. The finding only called
the second path out because the first one's `bool` looked deliberate, but it was answering "was there
anything to say", not "did it go out" — same defect, one branch further in. The `remaining <= 0` early
return is unchanged.

Pinned by `NudgeSweep_GraceWarning_SwallowedDispatch_LeavesPeriodUnmarkedAndRetriesNextRun`, which
needed a new `GraceDispatchFails` flag on the test double: its existing failure mode *throws*, and a
throw was already handled by the per-period catch, so it could not reproduce this. The swallow-and-
return-false path is the one that got past the guard.

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

### `CQ-8` 🟠 ✅ FIXED 2026-09-06 · A dormant period catches up one cycle per invocation

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

**Fixed 2026-09-06**, with `CQ-1`. `TryReset` keeps its one-cycle contract — the single-item overload's
whole design depends on it, and a caller that means one cycle should be able to say so — and the loop
lives in a new `RoutineResetService.TryResetCatchUp(period, items, now)` that all three reset sites now
call instead.

Departures from the finding's prescription, both deliberate:

- **The cap is not `HistoryDepth`, and nothing is jumped past.** Capping the *walk* at `HistoryDepth`
  would stop scoring cycles that really happened; the streak is meant to reflect all of them. The walk
  runs to completion (hard stop at 1200 cycles, ~three years of daily dormancy, purely as a guard
  against a clock-skewed `LastResetAt`) and it is the **completion rows** that are trimmed to the
  newest `HistoryDepth` before persisting — those are the only ones any read ever returns, so writing
  the rest is churn. The heatmap still shows the gap; it just does not show rows that fell off it.
- **`CheckGrace` runs inside the loop**, against the boundary being closed rather than against `now`.
  The old drip got this for free (each nightly run called `CheckGrace` with that day's `now`), so
  collapsing the cycles without it would have let a streak survive a grace window that lapsed two
  cycles ago.

Notifications collapse: the catch-up returns one `Latest` result and the three call sites notify on
that, so two months away is one summary, not sixty.

Pinned by six `TryResetCatchUp_*` unit cases (all cycles closed in one pass; nothing due → null;
completions trimmed to `HistoryDepth` and it is the *newest* that survive; `Latest` is the most
recently closed cycle; a lapsed grace from an earlier cycle breaks the streak; every cycle scored, not
just the first), and end-to-end by `ResetSweep_PeriodDormantForSeveralCycles_ClosesThemAllInOneRun`
plus `GroupedRead_PeriodDormantForSeveralCycles_CatchesThemAllUpInOneRead`.

`GroupedRead_ElapsedPeriod_TriggersResetAndStreakEvaluation` moved from a 30-day-old `LastResetAt` to
an 8-day one. It asserted `Streak == 1` and a single completion row on a 7-day period that had been
dormant for four cycles — true under the drip, wrong under the catch-up, where three of those four
cycles are genuine misses. The single-cycle case it was written for is what it pins now; the
multi-cycle case is the new test beside it.

`Doc impact: done — domain-map.md's Domain services table gains a TryResetCatchUp row, the TryReset list row says "closes one cycle", and the Reset rule spells out the catch-up`

### `CQ-13` 🟠 ✅ FIXED 2026-09-06 · Spending a freeze does not check grace first, so a lapsed window can zero the streak the freeze just bought

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

**Fixed 2026-09-06.** One line, exactly where the finding put it: `RoutineResetService.CheckGrace(period, now);`
immediately before the `Apply` call, with the ordering argument written down next to it so it is not
"tidied" to after.

Pinned by `RoutineStreakFreezeGraceTests.SpendFreeze_WithALapsedGraceWindow_ClearsIt_SoTheNextReadKeepsTheRestoredStreak`.
The assertion runs through a **second** request — a grouped read after the freeze — because the freeze
response was never the thing that was wrong. It reported the restored streak correctly both before and
after the fix; what changed is whether the next page load keeps it.

`Doc impact: domain-map.md → Business rules (Grace) — restate the rule as "before TryReset **and before Apply**", i.e. on every access, which is what it already says and what the code will then do` — done, together with `DOC-7`.

### `CQ-9` 🟡 ✅ RESOLVED 2026-09-07 (documented, rename declined) · `RoutinePeriodCompletion` is the one routines table outside the entity-base convention — and it shows in the schema

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

**Resolved 2026-09-07 — the rename is declined (`MIG-3`), the divergence is now written down.** No schema
change, no migration, no test-literal change: the ledger is append-only and never concurrently updated, so
the missing `row_version` buys nothing, and the plural name stays pinned exactly as it was. What changed is
that all four places a reader could hit this now say so, in the order they'd hit them:

- `RoutinePeriodCompletionConfiguration` gained class remarks naming the constraint that blocks the call
  (`IBaseTableEntity`), listing what the missing `BaseEntityConfigure()` costs (plural table, no
  `row_version`, identity instead of `serial`), and stating that a **new** convention added to
  `BaseEntityConfigure` skips this table — the exact failure mode this finding was about.
- `RoutinePeriodCompletion` gained class remarks giving the *reason* it is a plain `BaseEntity` (no `UserId`,
  never updated after the sweep writes it), plus a note on `CreatedAt` that it is this type's own timestamp
  and has no `now()` default — so nobody assumes the base pair is there.
- `ModuleSchemaTests`'s Routines literal gained a comment for why `routine_period_completions` is plural,
  since that literal is precisely where a future reader would otherwise "correct" it.
- `domain-map.md` records it in both the entity table and the Persistence table.

`Doc impact: domain-map.md → Persistence (RoutinePeriodCompletionConfiguration row: say it does not go through BaseEntityConfigure, and that the table is consequently plural and has no row_version)` — done, and the `RoutinePeriodCompletion` entity row too, since the entity's base class is where the constraint originates.

### `CQ-10` 🟡 ✅ FIXED 2026-09-07 · Two dead filter DTOs, both exposing a settable `UserId`

`application/dto/filter/RoutineTimePeriodFilterRequest.cs` ·
`application/dto/filter/RoutineToDoListFilterRequest.cs`

Neither type is referenced anywhere in the solution — a whole-repo search returns only the two
declarations, `review/portal/REMAINING.txt` and this review. The module has no grid or filter endpoint.
Both records expose `public long? UserId { get; set; }` — harmless while unused, but it is a
mass-assignment shape waiting for someone to wire a `BaseGridEndpoint` against it and hand a caller a
`userId` filter parameter.

**Fix.** Delete both. If a routine grid is planned, reintroduce them without `UserId` — scoping is the
DbContext's job here (invariant 3).

**Fixed 2026-09-07.** Both deleted through Rider's safe-delete, which re-ran the usage analysis and found
none, so the "whole-repo search" claim above is now machine-confirmed rather than grep-confirmed. The
`application/dto/filter/` directory went with them — it held nothing else.

`Doc impact: domain-map.md → the DTO paragraph says "two filters in dto/filter/"; drop that clause when the files go` — done, and it does not merely drop the clause: it records that there is no `dto/filter/` and that a reintroduced filter must not carry `UserId`, since the mass-assignment shape is the part worth not repeating.

### `CQ-11` 🟡 ✅ FIXED 2026-09-06 (half) · Both to-do list validators open a DI scope per validation, and duplicate 30 lines verbatim

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

**Fixed 2026-09-06, the duplication half; the scope stays on purpose.** The 30 lines now live once, in
`RoutineTodoListPeriodRules`, called from both validators in two lines each along with `SEC-1`'s
ownership predicate.

The `IServiceScopeFactory` was **not** replaced with an injected `DbContext`, and this finding was
wrong to ask for it: FastEndpoints resolves validators as singletons, so a constructor-injected scoped
`DbContext` is a captive dependency — one context for the process, shared across concurrent requests.
The scope is the correct shape here, not an oversight, and it is the shape all four ActivityProfiles
validators with a DB-backed rule use. What `SEC-1` actually needed from it was the *principal*, which
now comes from `IHttpContextAccessor` and is compared explicitly, so nothing depends on how the fresh
scope's `ScopeUserId` is populated. The remark on `RoutineTodoListPeriodRules` records this so it is
not "simplified" back.

`Doc impact: none` — but see `SEC-1`; the validators paragraph in `domain-map.md` gained the helper.

### `CQ-12` 🟡 ✅ FIXED 2026-09-07 · Three different user-scoping styles across the endpoints

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

**Fixed 2026-09-07 by the first branch — commenting, not standardising.** The two silent sites now carry a
doc comment naming the mechanism (`RoutineToggleIsDoneTodoListEndpoint.FetchAndPrepare`,
`ToggleStepIsDoneRoutineTodoListEndpoint.FetchItem`), each saying which entity is `IEntityWithUser`, which
queries the filter therefore covers, and what a foreign id produces — empty list → 404 in the batch
endpoint, null → 404 in the step one, both from the `TodoLists` bases. Each closes with why the comment
exists at all, since "the alternative reading is *forgot*" is the whole finding. The two `GetById`
endpoints already had their one-liner and were left alone; the style count is unchanged, but no site is
now silent about which one it is.

Converting the two toggles to explicit predicates was the other branch and was not taken: neither endpoint
holds a user id today (`RoutineToggleIsDoneTodoListEndpoint` never touches `User`), so the predicate form
would add a `User.GetId()` and a second scoping mechanism to paths the filter already covers correctly —
buying legibility that a comment buys for nothing. The freeze endpoint's predicate earns its keep for a
different reason, stated in its own comment: it makes the 404 an *ownership* answer.

`Doc impact: none`

### `CQ-14` 🟡 ✅ FIXED 2026-09-06 · A freeze can be spent on a period that had no items

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

**Fixed 2026-09-06**, as prescribed — the guard in `Apply` is now
`if (entry.TotalCount == 0 || IsSuccess(entry, period.StreakThreshold))`, with a comment saying why
`IsSuccess` returning false is not the same question as "is this a miss". Left in `Apply` rather than
folded into `IsSuccess`: `IsSuccess` is also what `RecomputeStreak` asks, and there the false answer is
already correct — an item-less row is skipped before the call.

Pinned by `RoutineStreakFreezeGraceTests.SpendFreeze_OnAPeriodThatElapsedWithNoItems_Refuses_AndChargesNothing`,
which asserts the budget is untouched and the row unfrozen, not just the 400.

`Doc impact: none` — but the `Apply` row in `domain-map.md`'s domain-services table now states the rule,
since it is a refusal the client cannot predict.

### `CQ-15` 🟡 ✅ FIXED 2026-09-06 · An item can be moved to another period without re-deriving its display order

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

**Fixed 2026-09-06**, as written: `DbContext.Entry(entity).Property(e => e.TimePeriodId).OriginalValue`
against the mapped value, and on a change `RoutineTodoListExtensions.GetNextDisplayOrder` — the create
path's own call — puts the item at the top of its destination. Reads with `SEC-2`, which stops the body
supplying `DisplayOrder` at all: without that fix this one would re-derive an order the same request had
just overwritten with a client's number.

Pinned in `RoutineTodoListUpdateEndpointTests`:
`UpdateRoutineTodoList_MovedToAnotherPeriod_ReDerivesDisplayOrder` (the destination holds a row at 1000
and the moved item lands above it) and `UpdateRoutineTodoList_SamePeriod_KeepsDisplayOrder` as the
control — re-deriving on every save would silently undo whatever the reorder endpoint last decided.

`Doc impact: none`

---

## 9. Performance hotspots

| Where | Issue | Impact | Mitigation | ID |
|---|---|---|---|---|
| `GetAllGroupedRoutineTodoListEndpoint.cs:76-91` | Loads the **entire** completion history for every period, then trims to `HistoryDepth` in memory (`:89`) | History grows one row per period per cycle forever; a two-year-old daily routine is ~730 rows fetched to render 16. Grows unboundedly with account age, on the module's most-hit read | `PARTITION BY … ORDER BY period_start DESC` in SQL, or a per-period `Take(HistoryDepth)` — the pattern `GetCompletionHistoryRoutineTimePeriodEndpoint.cs:42-47` already uses correctly | `PERF-1` ✅ |
| `GetAllGroupedRoutineTodoListEndpoint.cs:102` | `RoutineTodoListResponse.Projection(new[] { e }.AsQueryable()).Single()` — **per item** | Builds and interprets the whole projection expression tree once per item via LINQ-to-Objects, on a request that already holds the materialised graph | Project the list once outside the loop, or map with a plain function. The expression is only needed for the `IQueryable` path | `PERF-2` ✅ |
| `RoutineTodoListResetJobHandler.cs:34-37` | Loads every user's periods with `RoutineTodoListColl` + `Steps` into memory in one go | Whole-table materialisation with two levels of collection include; memory scales with total users × routines × steps. Also makes the single `SaveChangesAsync` at `:68` an all-or-nothing batch — see `SEC-3` | Page by user id and commit per page | `PERF-3` ✅ |
| `RoutinePeriodNudgeJobHandler.cs:40-43` | Same shape, plus `Where(p => !p.IsHidden)` with no supporting index | Sequential scan of `routine_time_period` daily. Small today; the sequential-notify loop below it is deliberate and documented (`:49-53`), so this is the only part worth changing | Page by user; see index note below | `PERF-4` ✅ |
| `RoutineStreakFreezeService.RecomputeStreak` via `SpendStreakFreezeRoutineTimePeriodEndpoint.cs:77-80` | Loads the complete history to recompute | **Deliberate and correct** — a truncated history under-counts the run, and the code says so at `:75-76`. Listed only so it is not "found" again later; its growth is bounded by fixing `PERF-1`, not this | none | — |

#### `PERF-1` ✅ FIXED 2026-09-07 — the history is trimmed by the database

The grouped read now fetches only the newest `HistoryDepth` completions per period, through one limited
subquery per period (`SelectMany` over `CompletionHistoryColl` with `OrderByDescending(PeriodStart).Take(depth)`,
which Npgsql renders as a LATERAL join) instead of the whole history plus an in-memory `Take`. The cost stops
growing with account age; the number of rows crossing the wire is now what the client renders.

`Take` has to be a constant per query, and `HistoryDepth` is per period, so the periods are **grouped by
depth** and each group runs one query — one or two in practice, since the column has a default and is rarely
edited. Not a windowed `PARTITION BY`, as the finding also offered: LINQ has no `ROW_NUMBER` and the raw SQL
would have to name the schema by hand (see CLAUDE.md), for a plan Postgres reaches the same way.

The per-period ordering does not survive the join, so the rows are re-sorted ascending after materialisation —
`RoutineTimePeriodResponse.From` documents that it takes the history oldest-first, and serving it reversed is a
heatmap drawn backwards, not an error.

Pinned by `RoutineGroupedReadAndMoveItemTests.GroupedRead_PeriodsWithDifferentHistoryDepths_EachTrimmedToItsOwnDepth`:
two periods with **different** depths (2 and 4), five completions each, asserted on the exact `PeriodStart`
sequence served. One depth would pass even if the code applied one period's depth to every period, and a count
assertion would pass on the oldest rows or on a reversed list.

`Doc impact: none`

#### `PERF-2` ✅ FIXED 2026-09-07 — one compiled mapping, not one interpreted tree per row

`RoutineTodoListResponse` and `RoutineTimePeriodResponse` now hold their projection as a static
`Expression<Func<TEntity, TResponse>>`. `Projection` passes it to `Select` unchanged; the new
`RoutineTodoListResponse.From` and the existing `RoutineTimePeriodResponse.From` run `.Compile()`d **once** in a
static field, so an already-materialised entity costs a delegate call rather than a one-element `IQueryable`
whose expression tree is re-interpreted per row. Same expression on both paths, so the SQL and in-memory shapes
cannot drift — which is why this is not a hand-written second mapper.

`RoutineTimePeriodResponse.From` was doing the same thing once per **period** on the same read (and on the flat
reads), so it was fixed with it; it is one construct, not two findings.

The compiled field is declared **after** the expression field in both files, with a comment saying why: static
field initializers run in textual order, so compiling above it captures a null and throws a
`NullReferenceException` on first use — a type-initializer failure on the module's most-hit read.

Covered by the existing grouped and flat read suites, which assert on the served field values; a mapping that
lost a field fails them.

`Doc impact: none`

#### `PERF-3` + `PERF-4` ✅ FIXED 2026-09-06 — both sweeps page by user

Both handlers now read the distinct user ids once up front and walk them a page at a time
(`infrastructure/jobs/RoutineSweepPaging.cs`, 100 users per page), so neither ever holds more than a page's
object graph — for the reset job, a page's periods *plus* their items and steps.

**Paged by user, not by row**, because a page is also the commit unit: the nudge sweep saves its
"notified for" markers per page, so a user split across two pages could be half-notified, and the reset job
clears the change tracker at each boundary. Skip/Take over periods would additionally drift as the sweep
mutates the rows it is ordering by. The reset job's commit stays **per period** — the SEC-3 fix — so paging
changed only where the tracker is released, never how much a single save carries.

`ChangeTracker.Clear()` at the page boundary is the one thing here that can fail silently: it is safe only
after every period on the page has been saved *and* announced, and `DiscardAsync`'s remark already records
why the mid-page case cannot use it. Pinned accordingly, on the rows rather than on the call counts, by
`RoutineTodoListResetJobHandlerTests.ResetSweep_AcrossPageBoundaries_ResetsEveryUsersPeriods` and
`RoutinePeriodNudgeJobTests.NudgeSweep_AcrossPageBoundaries_NotifiesAndMarksEveryUser`. Both force a boundary
by passing `userPageSize: 1` — a defaulted constructor parameter on each handler that only the tests supply
— rather than seeding a hundred users; the nudge case also keeps a hidden period in the seed, because the
`!IsHidden` filter now appears on two queries instead of one.

The batching half of `SEC-3` was already fixed with that finding; `PERF-3` claimed it too, and nothing more
was owed here.

`Doc impact: testing.md → guard rows` — done.

### Indexes that should exist

- `routine_period_completion (time_period_id, period_start DESC)` — the unique index on
  `(time_period_id, period_start)` already serves ordered lookups by period, so this is only worth
  adding if `PERF-1` is fixed with a windowed query that wants the descending order. **No action** —
  `PERF-1` landed as an ordered `LIMIT` per period, which walks that same unique index backwards. A btree
  reads in either direction, so a `DESC` copy would only duplicate it.
- ~~`routine_time_period (is_hidden)`, or a partial index `WHERE NOT is_hidden`~~ — ✅ **added 2026-09-06**
  with `PERF-3`'s paging, as this note asked. Landed as `ix_routine_time_period_user_id_visible`: keyed on
  `user_id` with `HasFilter("NOT is_hidden")`, because after the paging fix both of the nudge sweep's queries
  — the distinct user ids and the page fetch — are "visible periods, by user" rather than "not hidden"
  alone. The two unique indexes on the table also lead with `user_id`, but neither can skip the hidden rows.
  Migration `20260906215926_RoutineTimePeriodVisibleUserIndex`.
- **No missing index on the hot read path.** `routine_todo_list` already carries
  `(user_id, time_period_id)` as an explicit performance index
  (`RoutineTodoListConfiguration.cs:57`) and the unique `(user_id, time_period_id, activity_id)` above
  it (`:53-54`). The grouped read's cost is `PERF-1` and `PERF-2`, not index coverage — worth stating
  plainly so nobody "fixes" it with an index.

---

## Code/doc drift

### `DOC-1` — **resolved since the first pass.** The docs' folder paths are now correct, and `domain-map.md` carries an explicit note that the namespaces still contain a `todoList` segment the folders do not. Nothing to fix.

### `DOC-2` — **resolved since the first pass.** `summary.md` now says "the 23 routine endpoints", which matches the tree (2 settings + 5 time-period commands + 4 time-period queries + 6 to-do commands + 3 to-do queries + 3 step endpoints).

### `DOC-3` 🟠 ✅ FIXED 2026-09-07 · The merge remark asserts behaviour the schema forbids

`application/seam/RoutineTodoListActivityReferenceSource.cs:18-22` — see `CQ-2`. Both the code and the
comment need changing; the comment's *reasoning* (don't invent a streak-folding rule) should survive
into whatever collision rule is chosen.

**Fixed 2026-09-07 with `CQ-2`.** The remark keeps its reasoning verbatim — nothing is folded, and the
losing streak is dropped rather than maxed or summed — and now bounds the claim it was making: two
routines stay two routines *while they sit in different time periods*, which is the schema's call, not
the seam's. The collision rule itself is documented on `RepointAsync` rather than on the type, beside
the code that applies it.

`Doc impact: domain-map.md → Invariants (new merge-collision rule)` — done, as invariants 12 and 13.

### `DOC-4` 🟠 ✅ FIXED 2026-09-06 · Invariant 9 does not hold — there is a fourth surface that shows the freeze budget

`domain-map.md` → Invariants, row 9 ("The freeze budget is refilled by whoever reads it, not by a job")
and `summary.md`'s freeze gotcha, which names the three sites that do call `RefreshFreezeBudget`: the
grouped read, the nightly reset job, and `Apply` itself.

`GET /routine-time-period` and `GET /routine-time-period/{id}` also show `FreezesRemaining` /
`FreezeBudget` / `FreezeBudgetResetsAt` — they run `RoutineTimePeriodResponse.Projection`, which selects
all three (`:74-76`) — and refresh nothing. So does anything reading the nested period on
`GET /routine-todo-list` / `{id}`, except that one serves a hard `0` instead (`CQ-5`). The invariant is
the right rule; the code is what is wrong.

**Fixed 2026-09-06 in the code, as the finding argued** — all four surfaces now refresh, through
`RoutineTimePeriodReadRefresher` (`CQ-5`). The invariant was right and stands.

The doc did get one edit anyway, and it is not a retreat: invariant 9 and `summary.md`'s freeze gotcha
both **enumerated** the calling sites, so leaving them at "three sites do today" would have been stale
the moment the fourth landed. Both now name the grouped read, the nightly job, `Apply` and the four
flat reads via the helper, without a count to go stale next time.

`Doc impact: domain-map.md → Invariants (9) — no edit once CQ-5 is fixed, which is the argument for fixing the code rather than the doc`

### `DOC-5` 🟡 ✅ FIXED 2026-09-07 · A test name contradicts the documented `TryReset` contract

`AdhdTimeOrganizer.IntegrationTests/Services/RoutineResetServiceTests.cs:204` —
`TryReset_Single_AfterResetTime_ResetsItemAndUpdatesPeriod`. The single-item overload explicitly does
not update the period (`RoutineResetService.cs:161-169`), and `domain-map.md`'s Domain services table
restates that. The name asserts the opposite, and the body's `period.LastResetAt.Should().NotBeNull()`
(`:218`) is vacuous — the fixture built the period with `lastResetAt: UtcNow.AddDays(-8)`, so it would
pass if the method were deleted. See `TEST-10`.

Rename to `..._ResetsItemWithoutAdvancingLastResetAt` and assert `LastResetAt` is **unchanged**, which is
the actual guarantee and is currently unpinned.

**Fixed 2026-09-07**, both halves, under the prescribed name. The `lastResetAt` the fixture is built with
is now held in a local and asserted back — `period.LastResetAt.Should().Be(lastResetAt)` — so the
assertion fails if the single-item overload ever starts advancing the cycle, which the vacuous
`NotBeNull()` could not. The comment on it says why the omission is deliberate rather than missing:
advancing here would consume the reset with no streak evaluation and no `RoutinePeriodCompletion` row.
That is the same sentence `RoutineResetService.TryReset`'s own summary carries, so the test and the
contract now say one thing.

`RoutineResetServiceTests` is green, 50 of 50.

`Doc impact: none (fix the test)`

### `DOC-6` 🟡 ✅ FIXED 2026-09-07 · The docs say completions cascade on period delete, but omit that items block it

`domain-map.md` → Navigation index, `DeleteRoutineTimePeriodEndpoint` row: "Delete a period; completions
cascade". True (`RoutinePeriodCompletionConfiguration.cs:26-29`) but half the story:
`RoutineTimePeriod → RoutineTodoList` is `OnDelete(DeleteBehavior.Restrict)`, declared twice
(`RoutineTimePeriodConfiguration.cs:74-77` and `RoutineTodoListConfiguration.cs:47-50`), so deleting a
period that still has items is refused. `DbUtils` maps the FK violation to `DbForeignKeyError` →
**409** (`EndpointHelper.cs:14`), which is the right answer and is pinned by
`TodoListDeleteBehaviorTests.DeleteRoutineTimePeriod_WithItems_Returns409`. The behaviour is fine; the
doc row reads as though the delete always succeeds.

**Fixed 2026-09-07, doc only** — the behaviour was already right, and `Restrict` is the correct answer for
a delete that would otherwise take a user's routine items with it. The row now reads "Delete a period;
completions cascade, items `Restrict` — a period with items is a 409", so the two halves of the delete are
named together and the 409 is documented as the designed outcome rather than something a caller discovers.
Still pinned by `TodoListDeleteBehaviorTests.DeleteRoutineTimePeriod_WithItems_Returns409`.

`Doc impact: domain-map.md → Navigation index (DeleteRoutineTimePeriodEndpoint row: "completions cascade, items Restrict — a period with items is a 409")` — done, verbatim.

### `DOC-7` 🟡 ✅ FIXED 2026-09-06 · "CheckGrace before every access" is asserted as a rule and violated by three sites

`domain-map.md` → Business rules, **Grace**: "`CheckGrace` must run before `TryReset` on *every*
access — a lapsed window breaks the streak, and if only the reset job checked it, a streak would survive
indefinitely as long as nobody's period rolled over."

Three surfaces skip it: `GetAllRoutineTimePeriodEndpoint` and `GetByIdRoutineTimePeriodEndpoint` (they
serve `Streak` from a lapsed window — `CQ-5`), and `SpendStreakFreezeRoutineTimePeriodEndpoint`, where
skipping it is actively harmful (`CQ-13`). The rule is right; fix the code, and while doing so restate
the rule as "before `TryReset` **and before `Apply`**" so the freeze path is named rather than implied.

**Fixed 2026-09-06, code first.** All three sites now call `CheckGrace`: the two period reads through
`RoutineTimePeriodReadRefresher` (`CQ-5`), the freeze endpoint directly (`CQ-13`). The two flat
to-do-list reads were a fourth and fifth surface serving `Streak` on a nested period, and they go
through the same helper.

The rule itself is restated as the finding asked — "before `TryReset` **and before `Apply`**" — and
`Apply` is now named rather than implied, with the freeze-specific failure spelled out, because that is
the one case where skipping the check is actively harmful rather than merely stale. The
`CheckGrace` row in the domain-services table carries the same wording.

`Doc impact: domain-map.md → Business rules (Grace) — name Apply alongside TryReset` — done.

---

## Corrections to the first pass

Recorded so the same conclusions are not re-derived. Four claims in the first-pass file were wrong.

| First-pass claim | Status | What is actually true |
|---|---|---|
| `SEC-4` — "check-constraint violations return the raw Postgres message to the client" | **Withdrawn** | `DbUtils.HandleException` puts `pgEx.Message` in the Result's *third* argument (`Result.Error(type, errorMessage, exceptionMessage)` — `Result.cs:44`). Every base endpoint then calls `AddError(result.ErrorMessage!)`, i.e. only the generic second argument (`"Validation error during HandleAsync"`). The Postgres text is never sent. |
| `CQ-9` fix — "Replace with `builder.BaseEntityConfigure();` … verify the generated migration is empty" | **Wrong on both counts** | `BaseEntityConfigure<TEntity>` is constrained `where TEntity : class, IBaseTableEntity`; `RoutinePeriodCompletion : BaseEntity` does not satisfy it, so the call does not compile. And the migration would not be empty — it is a table rename (`routine_period_completions` → `routine_period_completion`), a `row_version` add and a `created_at` change. `CQ-9` above is rewritten accordingly. |
| `DOC-1` — "every folder path in the docs is stale"; `DOC-2` — "endpoint count disagrees (21 vs 23)" | **Both already fixed** | The docs were rewritten after the first pass. Paths are correct, `domain-map.md` carries an explicit "trust the path column, not the namespace" note, and `summary.md` says 23. |
| `DOC-4` cited `domain-map.md:152-154` and "invariant 5" for the freeze-refresh rule | **Wrong anchor** | That rule is **invariant 9** in the current `domain-map.md`; invariant 5 is the `(TimePeriodId, PeriodStart)` uniqueness. Re-anchored above. The underlying finding stands. |
