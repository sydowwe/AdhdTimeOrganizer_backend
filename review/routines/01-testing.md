# Routines — Test Review (01)

Point-in-time snapshot, **re-verified 2026-08-31** (second pass, deep). Disposable — the living docs are
`AdhdTimeOrganizer.Routines/docs/{summary,domain-map,testing}.md`.

> **The first pass under-counted coverage badly.** It missed four shared suites that cover routine
> routes without "Routine" in the filename: `TodoListCrudAuthMatrixTests`, `TodoListDeleteBehaviorTests`,
> `TodoListStepsCrudTests` and `BaseToggleIsHiddenEndpointTests`. Between them they cover
> create / update / delete / get-by-id on **both** routine entities with happy, IDOR and role cases —
> eight of the eleven endpoints the first pass called untested. The matrix below is rebuilt from a
> route-level search of the whole test project. Three of its fourteen backlog items are consequently
> withdrawn; see the end of this file.

## Test infrastructure summary

Everything lives in `AdhdTimeOrganizer.IntegrationTests` — this slice ships no test project of its own.
Two shapes are in use. The pure-domain tests (`Services/RoutineResetServiceTests`,
`Services/RoutineStreakFreezeServiceTests`) are plain xUnit classes over the two static services with
hand-built entities and no fixture at all, which is right since both services are pure and take `now` as
a parameter. Everything else derives from `PostgresTestBase(AppDbContextFixture)` under
`[Collection("Postgres")]`, using `CreateDbContext()` for out-of-band seed/verify, `CreateClient()` /
`CreateUserRoleClient()` for HTTP, `FakeLoggedUserService.TestUserId` as the ambient user, and
`ReminderSeedHelper.EnsureOtherUserAsync` / `OtherUserId` to materialise a real second user row for IDOR
fixtures (the `user_id` FK is NOT NULL, so an unowned id will not do).

Bespoke and worth knowing about:

- **`RunJobAsync`** (`RoutineTodoListResetJobHandlerTests.cs:107`) builds the handler with
  `ActivatorUtilities` inside a fresh scope and hand-rolls a `ScheduledJobContext`, reproducing what the
  Scheduler dispatcher does without booting Quartz. `RoutinePeriodNudgeJobTests` does the same. Correct,
  and — contrary to the first pass — the registrar is **not** left unexercised:
  `ModuleWiringTests.BootRegistrars_RegisterBothModulesRecurringJobs` boots the real host and asserts
  both `RoutinesScheduledJobsRegistrar.ResetRegistration.JobKey` and `.NudgeRegistration.JobKey` are on
  the schedule (`:348-352`), and `ScheduledJobHandlers_AreAllDiscoverableByKey` pins both handler keys
  (`:151-152`). That is the documented "fifth registration point" and it is guarded.
- **`UnauthenticatedLoggedUserService`** (`RoutineTodoListResetJobHandlerTests.cs:410`) is genuinely good
  fixture work: the default `FakeLoggedUserService` hard-codes `IsAuthenticated => true`, which would
  populate `AppDbContext.ScopeUserId` and quietly defeat the "sweep sees every user" test. Swapping in an
  unauthenticated stub is what makes that assertion mean anything.
- **`RoutinePeriodNotificationServiceDouble`** (`RoutinePeriodNudgeJobTests.cs:41`) is a hand-written
  double rather than a Moq mock, recording calls so idempotency markers can be asserted across two runs,
  and able to throw on the *n*-th call. `RoutineTodoListResetJobHandlerTests` uses Moq for its one
  throwing-notifier case. Mixed, but each choice fits its test.
- **Fragility to watch:** `RoutineTimePeriod` carries unique indexes on `(UserId, Text)` *and*
  `(UserId, LengthInDays)`, so every seeded period needs a distinct length as well as a distinct text.
  The suites handle this with `Guid.NewGuid()` texts and hand-picked lengths and several carry a comment
  explaining the choice — but it is a trap for any new test, enforced only by a confusing 409 at seed
  time. `TodoListTestSeedHelper.SeedRoutineTimePeriodAsync` / `SeedRoutineTodoListAsync` exist precisely
  to absorb this; use them.

## 7. Test quality assessment

**Strengths.** This remains the best-tested module in the solution, and the second read raised rather
than lowered that assessment. Assertions are overwhelmingly on *rows and recomputed state* rather than
status codes — which is what CLAUDE.md's "Silent failures" section demands — and the suites say so in
their own header comments instead of leaving it implicit.

- `RoutineStreakFreezeTests` verifies through a fresh `CreateDbContext()` after the HTTP call that the
  refill was **persisted, not merely projected** (`:275-297`) — the exact bug a response-only assertion
  would miss.
- `RoutineTodoListResetJobHandlerTests.ResetSweep_UnticksTheItemAndAllOfItsSteps` re-reads through a
  fresh context specifically so a query shape that forgets `.Include(Steps)` cannot be masked by the
  seeding context's tracked graph. A real, considered isolation decision, not boilerplate.
- `ResetSweep_DoubleFire_ProducesExactlyOneCompletionRowAndOneStreakAdvance` pins idempotency on the row
  count; `ResetSweep_PersistsGraceExpiry_WhenNoPeriodIsDueForReset` and
  `..._AlongsideAPeriodThatIsDueForReset` are a matched pair covering both sides of the save-gating
  condition at `RoutineTodoListResetJobHandler.cs:61`. Those two are clearly guarding a bug that was hit
  before.
- The freeze suite covers every `StreakFreezeRejection` branch, both grace-window cases (newest vs older
  entry), the lazy-refill boundary including a December rollover, and IDOR — with a comment on each
  explaining why the failure would otherwise be silent.
- `RoutineGroupedReadAndMoveItemTests.GroupedRead_ElapsedPeriod_TriggersResetAndStreakEvaluation`
  asserts four separate downstream facts (advanced `LastResetAt`, `Streak == 1`, exactly one completion
  row, item un-ticked) rather than a 200. That is the house style working as intended.
- `NudgeSweep_IgnoresUserTimezone_IdenticalUtcPeriodsProduceIdenticalResultsRegardlessOfOffset` pins the
  "everything is UTC" rule against someone helpfully adding zone conversion later.
- AAA structure and isolation are clean throughout; seeding happens in a disposed scope before the act.

**Weaknesses.**

1. **Assertion theater — `TryReset_Single_AfterResetTime_ResetsItemAndUpdatesPeriod`**
   (`RoutineResetServiceTests.cs:204`). The name says "UpdatesPeriod"; the assertion is
   `period.LastResetAt.Should().NotBeNull()` (`:218`). The single-item overload **deliberately does not
   touch `LastResetAt`** — that is its entire documented contract (`RoutineResetService.cs:161-169`).
   The assertion passes only because the fixture built the period with `lastResetAt: UtcNow.AddDays(-8)`.
   It would pass if the method body were deleted. → `TEST-10`, `DOC-5`
2. **The `ComputeNextReset` monthly/yearly tests are structurally incapable of catching `CQ-1`.**
   `ComputeNextReset_MonthlyPeriod_SnapsToTargetDayOfNextMonth` (`:108-119`) and
   `..._YearlyPeriod_SnapsToSameDayNextYear` (`:122-133`) both pass `now == lastReset`, so the
   `while (candidate < now)` catch-up loop never executes. That loop is where `CQ-1` lives — a monthly or
   yearly period with a non-zero anchor never resets at all, which is every user's two seeded defaults.
   The tests are green and the bug is total. → `TEST-1`
3. **`RoutineTimePeriodRangeConstraintTests` certifies a guarantee it cannot observe.** Its own summary
   says it pins "a clean 400 from FluentValidation rather than a 500 from
   `RoutineTimePeriodConfiguration`'s matching DB check constraints" (`:10-13`). But
   `RoutineTimePeriodValidator` has **no `HistoryDepth` rule** and **no upper bound on `LengthInDays`**,
   so `HistoryDepth_Zero`, `HistoryDepth_101` and `LengthInDays_366` are green only because
   `DbUtils.HandleException` maps `CheckViolation` → `ValidationError` → 400. Three of the ten cases
   assert the opposite layer from the one the file claims to be testing, and nothing distinguishes them.
   → `TEST-4`, `CQ-3`
4. **The module's one genuine cross-tenant write path is untested.** Nothing asserts that
   `POST` / `PUT /routine-todo-list` rejects a `TimePeriodId` belonging to another user. The auth matrix
   covers *entity* ownership on both endpoints thoroughly — it does not cover the foreign **foreign key**
   in the body, which is a different hole. → `TEST-3`, `SEC-1`
5. ~~**The routine toggle endpoint has no test of any kind.**~~ ✅ **Closed 2026-09-07** by
   `Routines/RoutineToggleIsDoneTests.cs` (`TEST-8`/`TEST-9`, 11 cases). As written, the gap read:
   `PATCH /routine-todo-list/toggle-is-done` is the single densest endpoint in the module — it runs the
   list `TryReset`, inserts a completion row, updates the item streak, and publishes the only cross-slice
   event this slice raises — and a route-level search of the whole test project finds zero hits.
   `CompletionFanOutTests` exercises `api/todo-list-item/toggle-is-done`, i.e. the TodoLists sibling, not
   this one. `CQ-7` (streak inflation) and half of `SEC-3` (unhandled 500 on the reset race) both live
   behind it.
   **Vindicated on writing:** the step half of the same surface turned out to be scoring no streak at all
   (`CQ-11`, todo-lists), a defect that had been sitting behind the zero hits and that a review of the
   Routines slice alone could not see, because the dead call was in a TodoLists base. `SEC-3`'s half is
   still open — nothing here asserts the unhandled 500 on the reset race. → `TEST-7`
6. **The flat read endpoints are untested and malformed.** `GET /routine-time-period`,
   `GET /routine-time-period/{id}`'s *shape* (its auth is covered), `GET /routine-todo-list`,
   `GET /routine-time-period/all-options/` — no test asserts what any of them actually returns. They are
   one-liners over framework bases, which is why they were skipped, but they serve
   `RoutineTimePeriodResponse` built by the bare `Projection`, so they emit `NextResetAt = 0001-01-01`,
   an empty `CompletionHistory` and an unrefreshed freeze count. One shape assertion would have caught it.
   → `TEST-6`, `CQ-5`
7. **Flakiness risk, low but non-zero.** Many endpoint tests use `DateTime.UtcNow` as the act-time rather
   than an injected instant, so a period whose reset instant lands on the day boundary mid-run could
   flip. `RoutinePeriodNudgeJobTests:484` already documents one such boundary and explains, correctly,
   why it chose a comment over a flaky assertion — that judgement is right and worth imitating.

## 8. Coverage matrix

### Endpoints

Counts are distinct test methods that exercise the route. "Auth/IDOR" means an assertion about another
user's row or an unauthenticated/role-restricted caller.

| Endpoint | Happy | Edge | Auth/IDOR | Test file |
|---|---|---|---|---|
| `POST /routine-time-period` | ✅ 3 | ✅ 12 | ✅ 2 | `RoutineTimePeriodUniqueIndexTests`, `RoutineTimePeriodRangeConstraintTests`, `TodoListCrudAuthMatrixTests` |
| `PUT /routine-time-period/{id}` | ✅ 1 | ❌ | ✅ 2 | `TodoListCrudAuthMatrixTests` (`UpdateRoutineTimePeriodEndpointTests`) — generic base cases only; the freeze-budget clamp at `RoutineTimePeriodRequest.cs:80` is unpinned → `TEST-11` |
| `DELETE /routine-time-period/{id}` | ✅ 1 | ✅ 1 | ✅ 2 | `TodoListCrudAuthMatrixTests`, `TodoListDeleteBehaviorTests.DeleteRoutineTimePeriod_WithItems_Returns409` |
| `PATCH /routine-time-period/toggle-is-hidden` | ✅ 1 | ⚠ generic | ⚠ generic | `BaseToggleIsHiddenEndpointTests` (drives the base through this endpoint) |
| `GET /routine-time-period` | ❌ | ❌ | ❌ | — → `TEST-6` |
| `GET /routine-time-period/{id}` | ⚠ auth only | ❌ | ✅ 2 | `TodoListCrudAuthMatrixTests` — **response shape unasserted** → `TEST-6` |
| `GET /routine-time-period/all-options/` | ❌ | ❌ | ❌ | — → `TEST-6` |
| `GET /routine-time-period/{id}/completion-history` | ✅ 1 | ✅ 1 | ✅ 1 | `RoutinePeriodCompletionHistoryTests` |
| `POST /routine-todo-list/time-period/{id}/streak-freeze` | ✅ 3 | ✅ 5 | ✅ 2 | `RoutineStreakFreezeTests` |
| `POST /routine-todo-list` | ✅ 1 | ❌ | ✅ 2 | `TodoListCrudAuthMatrixTests` — **foreign `TimePeriodId` unasserted** → `TEST-3` |
| `PUT /routine-todo-list/{id}` | ✅ 1 | ❌ | ✅ 2 | `TodoListCrudAuthMatrixTests` — step churn (`CQ-4`) and `IsDone`-without-event (`SEC-2`) unasserted → `TEST-3`, `TEST-5` |
| `DELETE /routine-todo-list/{id}` | ✅ 1 | ❌ | ✅ 2 | `TodoListCrudAuthMatrixTests` |
| `GET /routine-todo-list` | ❌ | ❌ | ❌ | — → `TEST-6` |
| `GET /routine-todo-list/{id}` | ⚠ auth only | ❌ | ✅ 2 | `TodoListCrudAuthMatrixTests` — nested-period shape unasserted → `TEST-6` |
| `GET /routine-todo-list/grouped-by-time-period` | ✅ 3 | ✅ 1 | ✅ 1 | `RoutineGroupedReadAndMoveItemTests`, `RoutineStreakFreezeTests` |
| `PATCH /routine-todo-list/toggle-is-done` | ✅ 2 | ✅ 4 | ✅ 1 | `RoutineToggleIsDoneTests` — **written 2026-09-07** (`TEST-8`); was "nothing at all" |
| `PATCH /routine-todo-list/{itemId}/steps/{stepId}/toggle` | ✅ 1 | ✅ 2 | ✅ 1 | `RoutineToggleIsDoneTests` — **written 2026-09-07** (`TEST-9`); found and fixed a live defect, see below |
| `PATCH /routine-todo-list/change-display-order` | ❌ | ❌ | ❌ | — the reorder tests target `todo-list-item`, not this → `TEST-6` |
| `POST /routine-todo-list/{itemId}/steps` | ✅ 1 | ❌ | ❌ | `TodoListStepsCrudTests.CreateStep_OnRoutineTodoList_AlsoWorks` — one deliberate cross-check that the shared base behaves identically |
| `PUT` / `DELETE /routine-todo-list/{itemId}/steps/{stepId}` | ⚠ base only | ⚠ base only | ⚠ base only | `TodoListStepsCrudTests` via `TodoListItem`; the routine overrides add an explicit `UserId` predicate in `GetParentQuery` that nothing exercises |
| `GET /routine/settings` | ✅ 2 | ✅ 1 | ✅ 2 | `UserRoutineSettingsTests` |
| `PUT /routine/settings` | ✅ 2 | ✅ 2 | ✅ 1 | `UserRoutineSettingsTests` |

### Services / handlers / jobs / seeders / seams

| Unit | Tests | Notes |
|---|---|---|
| `RoutineResetService` | 38 | Thorough on the weekly path; the calendar-aligned branches are covered in name only → `TEST-1`. One vacuous assertion → `TEST-10` |
| `RoutineStreakFreezeService` | 15 | Every branch of `Apply` and `RecomputeStreak`. The `TotalCount == 0` case of `Apply` (`CQ-14`) is not among them |
| `RoutineTodoListResetJobHandler` | 10 | Query shape, save gating (both sides), idempotency, streak outcomes, cross-user sweep, notifier failure |
| `RoutinePeriodNudgeJobHandler` | 13 | Both notifications, both idempotency marks, hidden periods, mid-loop failure, ceiling-not-floor, UTC invariance |
| `RoutinePeriodNotificationService` | 5 | `RoutineNotificationTests` — mapping and the "already done → unmarked" asymmetry |
| `RoutineTimePeriodSeeder` | 2+ | `RoutineTimePeriodUniqueIndexTests.Seeder_SetupDefaults_…`, plus `PerUserDefaultSeederTests` / `PerUserDefaultMatcherTests` |
| `RoutineTodoListSeeder` (dev) | 0 | → `TEST-13` |
| `RoutineTodoListActivityMembershipSource` | ✅ | `SeamWiringTests.ActivityMembershipSources_CoverEveryKey_Exactly` + History's grid tests |
| `RoutineTodoListActivityReferenceSource` | 3 | `ActivityMergeTests` — the single-routine repoint, plus the same-period collision and its different-periods control (`TEST-2`, added with the `CQ-2` fix) |
| `RoutinesScheduledJobsRegistrar` | 2 | `ModuleWiringTests` `:151-152` and `:348-352`. **Covered** — the first pass was wrong about this |
| `RoutineTodoListExtensions` | indirect | Only through `CreateRoutineTodoListEndpoint`'s create-path tests |
| Schema / export classification | ✅ | `ModuleSchemaTests` (all four tables → `routines`), `UserDataExportAndDeletionTests` (two entities on the documented `KnownGaps` list) |

### Honest coverage number

**About 75% of the module is meaningfully covered, and the shape of the remaining 25% is what matters
more than the number.** The domain core — the two static services, both job sweeps, the freeze endpoint,
the grouped read — is at ~90% and is genuinely well-pinned, with the single structural exception of the
calendar-aligned `ComputeNextReset` branches. The CRUD surface is at ~70% once the shared auth matrix is
counted, and its coverage is uniformly *generic*: the framework base cases (happy path, foreign id,
role) are pinned on every endpoint, and every routine-specific behaviour layered on top of those bases
is not.

**The riskiest gaps, in order:**

1. **The toggle endpoint has nothing.** It is the densest endpoint in the module and the only publisher
   of the cross-slice event; two open findings (`CQ-7`, `SEC-3`) sit behind an untested route.
2. **The `ComputeNextReset` catch-up loop**, where the existing tests are shaped so as to be
   structurally incapable of catching a total failure of monthly and yearly resets on every account.
3. **The create/update ownership hole** (`SEC-1`) — a live cross-tenant write with no test on any path,
   hidden by an auth matrix that looks comprehensive because it covers a *different* ownership question.
4. **The activity-merge collision**, which throws on a user-facing six-slice transaction and whose
   existing test seeds exactly one routine, one row short of the failing case.

## 12. Missing tests — backlog

Prompt files live in `review/routines/testingPrompts/`; each is self-contained for a context-less agent
and assumes only `_SHARED-CONTEXT.md`.

| ID | Gap | Prompt file |
|---|---|---|
| `TEST-1` ✅ | **Written 2026-09-06** with the `CQ-1` / `CQ-8` fix — both pure-domain and sweep-level, plus six `TryResetCatchUp_*` cases the prompt did not anticipate | `ComputeNextResetCatchUp.md` |
| `TEST-2` ✅ | **Written 2026-09-07** with the `CQ-2` fix — the collision case plus the different-periods control the prompt asked for | `ActivityMergeRoutineCollision.md` |
| `TEST-3` | Create/Update a routine item with a **foreign** `TimePeriodId` — `SEC-1` | `RoutineTodoListCrud.md` (§1) |
| `TEST-4` ✅ | **Written 2026-09-07** with the `CQ-3` fix — all ten cases now assert the error's field key, plus the two top-of-range cases that keep the new rules honest | `ValidatorVsDbConstraint.md` (§1) |
| `TEST-5` | `PUT /routine-todo-list` flipping `IsDone`: no event, no streak, steps re-keyed — `SEC-2`, `CQ-4` | `RoutineTodoListCrud.md` (§2, §3) |
| `TEST-6` | Flat read endpoints' response shape and freshness — `CQ-5`, `DOC-4` | `RoutineReadEndpointShape.md` |
| `TEST-7` | Concurrent reset writing a duplicate `RoutinePeriodCompletion` — `SEC-3` | `ConcurrentResetRace.md` |
| `TEST-8` ✅ | **Written 2026-09-07** as `Routines/RoutineToggleIsDoneTests.cs`. The prompt was stale: `CQ-7` had already been fixed and its two pure-domain tests already existed, so §1 landed as green endpoint-level pins over that fix rather than as expected-failing cases | `ToggleIsDoneStreak.md` (§1–§3) |
| `TEST-9` ✅ | **Written 2026-09-07**, same file. Found a live defect the prompt did not anticipate: the step route reached `UpdateItemStreak` **never**, because TodoLists' `BaseToggleStepIsDoneEndpoint` had its only call to `OnItemCompleted` commented out. Fixed the same day by restoring it — see `CQ-11` in [`review/todo-lists/02-findings.md`](../todo-lists/02-findings.md) | `ToggleIsDoneStreak.md` (§4) |
| `TEST-10` ✅ | **Written 2026-09-07** with the `DOC-5` fix — renamed, and the vacuous `NotBeNull()` replaced by an assertion that `LastResetAt` is unchanged | `ValidatorVsDbConstraint.md` (§2) |
| `TEST-11` | `PUT /routine-time-period` — freeze-budget clamp when the budget is lowered mid-window | `RoutineTimePeriodUpdate.md` |
| `TEST-12` | Freezing an older miss while a lapsed grace window is still open — `CQ-13` | `FreezeAfterLapsedGrace.md` |
| `TEST-13` | `RoutineTodoListSeeder` (dev) — truncate scope and per-user seeding | `RoutineDevSeeder.md` |

### Withdrawn from the first pass

| Was | Why withdrawn |
|---|---|
| `TEST-9` "auth/IDOR matrix for the untested endpoints" | `TodoListCrudAuthMatrixTests` already covers create/update/delete/get-by-id on both routine entities with foreign-id and role cases. Prompt `RoutineAuthMatrix.md` deleted. |
| `TEST-12` "delete behaviour on both routine entities" | `TodoListDeleteBehaviorTests.DeleteRoutineTimePeriod_WithItems_Returns409` pins the `Restrict` FK exactly. Prompt `RoutineDeleteBehaviour.md` deleted. |
| `TEST-14` "registrar present in the composition root" | `ModuleWiringTests.BootRegistrars_RegisterBothModulesRecurringJobs` asserts both job keys against a booted host. Prompt `ScheduledJobsRegistrarWiring.md` deleted. |
