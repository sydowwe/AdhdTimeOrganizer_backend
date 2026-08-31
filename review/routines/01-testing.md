# Routines — Test Review (01)

Point-in-time snapshot, 2026-08-31. Disposable — the living docs are
`AdhdTimeOrganizer.Routines/docs/{summary,domain-map}.md`.

## Test infrastructure summary

Everything lives in `AdhdTimeOrganizer.IntegrationTests` (the host's suite — this slice ships no test
project of its own). Two shapes are in use. The pure-domain tests
(`Services/RoutineResetServiceTests`, `Services/RoutineStreakFreezeServiceTests`) are plain xUnit
classes over the two static services with hand-built entities and no fixture at all — appropriate,
since both services are pure. Everything else derives from `PostgresTestBase(AppDbContextFixture)`
under `[Collection("Postgres")]`, using `CreateDbContext()` for out-of-band seed/verify, the
`CreateClient()` / `CreateUserRoleClient()` helpers for HTTP, `FakeLoggedUserService.TestUserId` as
the ambient user, and `ReminderSeedHelper.EnsureOtherUserAsync` / `OtherUserId` to materialise a real
second user row for IDOR fixtures (the `UserId` FK is NOT NULL, so an unowned id is not enough).

Bespoke and worth knowing about:

- **`RunJobAsync`** in `RoutineTodoListResetJobHandlerTests.cs:107` builds the handler with
  `ActivatorUtilities` inside a fresh scope and hand-rolls a `ScheduledJobContext` — it reproduces
  what the Scheduler dispatcher does without booting Quartz. `RoutinePeriodNudgeJobTests` does the
  same. Correct, but it means **the registrar is never exercised**: nothing asserts that
  `RoutinesScheduledJobsRegistrar` is actually in `Program.cs`, which is one of the documented
  silent-failure modes (summary.md's "fifth registration point").
- **`UnauthenticatedLoggedUserService`** (`RoutineTodoListResetJobHandlerTests.cs:410`) is a genuinely
  good piece of fixture work: the default `FakeLoggedUserService` hard-codes `IsAuthenticated => true`,
  which would scope `AppDbContext.ScopeUserId` and quietly defeat the "sweep sees every user" test.
  Swapping in an unauthenticated stub is what makes that assertion mean anything.
- **`RoutinePeriodNotificationServiceDouble`** (`RoutinePeriodNudgeJobTests.cs:41`) is a hand-written
  double rather than a Moq mock, recording calls so the sweep's idempotency markers can be asserted
  across two runs. `RoutineTodoListResetJobHandlerTests` uses Moq for the one throwing-notifier case.
  Mixed, but each choice fits its test.
- **Fragility to watch:** `RoutineTimePeriod` carries unique indexes on `(UserId, Text)` *and*
  `(UserId, LengthInDays)`, so every seeded period needs a distinct length as well as a distinct text.
  The suites handle this with `Guid.NewGuid()` texts and hand-picked lengths, and several tests carry a
  comment explaining the choice — but it is a trap for any new test, and it is enforced only by a
  confusing 409 at seed time.

## 7. Test quality assessment

**Strengths.** This is, by some distance, the best-tested module I have read in this solution. The
assertions are overwhelmingly on *rows and recomputed state*, not on status codes — which is exactly
what CLAUDE.md's "silent failures" section demands, and the test files say so in their own header
comments rather than leaving it implicit. Specific things done right:

- `RoutineStreakFreezeTests` verifies through a fresh `CreateDbContext()` after the HTTP call that the
  refill was **persisted, not just projected** (`:297`) — the exact bug a response-only assertion would
  miss.
- `RoutineTodoListResetJobHandlerTests.ResetSweep_UnticksTheItemAndAllOfItsSteps` re-reads through a
  fresh context specifically so a query shape that forgets `.Include(Steps)` cannot be masked by the
  seeding context's tracked graph (`:140`). That is a real, considered isolation decision.
- `ResetSweep_DoubleFire_ProducesExactlyOneCompletionRowAndOneStreakAdvance` pins idempotency on the
  row count, and `ResetSweep_PersistsGraceExpiry_WhenNoPeriodIsDueForReset` /
  `..._AlongsideAPeriodThatIsDueForReset` are a matched pair covering both sides of the save-gating
  condition. Those two are guarding a bug that was clearly hit before.
- The freeze suite covers every `StreakFreezeRejection` branch, both grace-window cases (newest vs.
  older entry), the lazy-refill boundary, and IDOR — with a comment on each explaining why the failure
  would otherwise be silent.
- AAA structure and isolation are clean throughout; seeding happens in a disposed scope before the act.

**Weaknesses.**

1. **Assertion theater — `TryReset_Single_AfterResetTime_ResetsItemAndUpdatesPeriod`**
   (`RoutineResetServiceTests.cs:204`). The name says "UpdatesPeriod" and the test asserts
   `period.LastResetAt.Should().NotBeNull()`. The single-item overload **deliberately does not touch
   `LastResetAt`** — that is its whole documented contract (`RoutineResetService.cs:161-169`). The
   assertion passes only because the fixture constructed the period with
   `lastResetAt: UtcNow.AddDays(-8)`. It would pass if the method were deleted. → `TEST-10`
2. **The `ComputeNextReset` monthly/yearly tests never cross the boundary that matters.**
   `ComputeNextReset_MonthlyPeriod_SnapsToTargetDayOfNextMonth` (`:108`) and
   `..._YearlyPeriod_SnapsToSameDayNextYear` (`:122`) both pass `now == lastReset`, so the
   `while (candidate < now)` catch-up loop never executes. That loop is where `CQ-1` lives — a monthly
   or yearly period with a non-zero anchor never resets at all. The tests are green and the bug is
   total. → `TEST-1`
3. **`RoutineTimePeriodRangeConstraintTests` asserts the wrong mechanism.** Its own summary says it
   pins "a clean 400 from FluentValidation rather than a 500 from ... DB check constraints"
   (`:10-13`). But `RoutineTimePeriodValidator` has **no `HistoryDepth` rule at all** and **no upper
   bound on `LengthInDays`**, so `HistoryDepth_Zero`, `HistoryDepth_101` and `LengthInDays_366` are
   green only because `DbUtils.HandleException` maps `PostgresErrorCodes.CheckViolation` to
   `ValidationError` → 400. The tests cannot tell the two layers apart, so they certify a guarantee
   that does not hold. → `TEST-4`, and see `CQ-3`.
4. **No test crosses the module's one genuine cross-tenant write path.** Nothing asserts that
   `POST`/`PUT /routine-todo-list` rejects a `TimePeriodId` belonging to another user. → `TEST-3`,
   `SEC-1`.
5. **The read endpoints are essentially untested.** `GetAllRoutineTodoListEndpoint`,
   `GetByIdRoutineTodoListEndpoint`, `GetAllRoutineTimePeriodEndpoint`,
   `GetByIdRoutineTimePeriodEndpoint` and `GetSelectOptionsRoutineTimePeriodEndpoint` have no test of
   any kind. They are one-liners over framework bases, which is why they were skipped — but they serve
   a `RoutineTimePeriodResponse` built by the bare `Projection`, so they emit
   `NextResetAt = 0001-01-01` and zeroed freeze fields (`CQ-5`). A single shape assertion would have
   caught it. → `TEST-6`
6. Flakiness risk is low but non-zero: many tests use `DateTime.UtcNow` as the act-time rather than an
   injected instant, so a period whose reset instant lands on the day boundary mid-run could flip.
   `RoutinePeriodNudgeJobTests:484` already documents one such boundary it chose not to assert on.

## 8. Coverage matrix

### Endpoints

| Endpoint | Happy | Edge | Auth/IDOR | Test file |
|---|---|---|---|---|
| `POST /routine-time-period` | ✅ 3 | ✅ 10 | ✅ 1 | `RoutineTimePeriodUniqueIndexTests`, `RoutineTimePeriodRangeConstraintTests` |
| `PUT /routine-time-period/{id}` | ❌ | ❌ | ❌ | — → `TEST-11` |
| `DELETE /routine-time-period/{id}` | ❌ | ❌ | ❌ | — → `TEST-12` |
| `PATCH …/toggle-is-hidden` | ✅ 1 | ❌ | ❌ | `BaseToggleIsHiddenEndpointTests` (generic) |
| `GET /routine-time-period` | ❌ | ❌ | ❌ | — → `TEST-6` |
| `GET /routine-time-period/{id}` | ❌ | ❌ | ❌ | — → `TEST-6` |
| `GET /routine-time-period/select-options` | ❌ | ❌ | ❌ | — → `TEST-6` |
| `GET /routine-time-period/{id}/completion-history` | ✅ 1 | ✅ 1 | ✅ 1 | `RoutinePeriodCompletionHistoryTests` |
| `POST …/time-period/{id}/streak-freeze` | ✅ 3 | ✅ 5 | ✅ 2 | `RoutineStreakFreezeTests` |
| `POST /routine-todo-list` | ❌ | ❌ | ❌ | — → `TEST-3` |
| `PUT /routine-todo-list/{id}` | ❌ | ❌ | ❌ | — → `TEST-3`, `TEST-5` |
| `DELETE /routine-todo-list/{id}` | ❌ | ❌ | ❌ | — → `TEST-12` |
| `GET /routine-todo-list` | ❌ | ❌ | ❌ | — → `TEST-6` |
| `GET /routine-todo-list/{id}` | ❌ | ❌ | ❌ | — → `TEST-6` |
| `GET /routine-todo-list/grouped-by-time-period` | ✅ 3 | ✅ 1 | ✅ 1 | `RoutineGroupedReadAndMoveItemTests`, `RoutineStreakFreezeTests` |
| `PATCH …/toggle-is-done` (bulk) | ❌ | ❌ | ❌ | — → `TEST-8` |
| `PATCH …/steps/toggle-is-done` | ❌ | ❌ | ❌ | — → `TEST-8` |
| `PATCH …/change-display-order` | ✅ 2 | ✅ 1 | ✅ 1 | `RoutineGroupedReadAndMoveItemTests` (move-item) |
| `POST/PUT/DELETE …/steps` | ⚠ generic | ❌ | ❌ | `TodoListStepsCrudTests` (base class, not routine-specific) |
| `GET /routine/settings` | ✅ 2 | ✅ 1 | ✅ 2 | `UserRoutineSettingsTests` |
| `PUT /routine/settings` | ✅ 2 | ✅ 2 | ✅ 1 | `UserRoutineSettingsTests` |

### Services / handlers / jobs / seeders / seams

| Unit | Tests | Notes |
|---|---|---|
| `RoutineResetService` | 30 | Thorough, but see `TEST-1` / `TEST-10` |
| `RoutineStreakFreezeService` | 13 | Every branch of `Apply` + `RecomputeStreak` covered |
| `RoutineTodoListResetJobHandler` | 9 | Query shape, save gating, idempotency, cross-user sweep, notifier failure |
| `RoutinePeriodNudgeJobHandler` | 13 | Both notifications, idempotency, hidden periods, mid-loop failure, DST/offset |
| `RoutinePeriodNotificationService` | 5 | Via `RoutineNotificationTests` |
| `RoutineTimePeriodSeeder` | 2 | `RoutineTimePeriodUniqueIndexTests`, `PerUserDefaultSeederTests` |
| `RoutineTodoListSeeder` (dev) | 0 | → `TEST-13` |
| `RoutineTodoListActivityMembershipSource` | ✅ | `SeamWiringTests` + History grid tests |
| `RoutineTodoListActivityReferenceSource` | 1 | `ActivityMergeTests` — single-routine repoint only; the collision case is untested → `TEST-2`, `CQ-2` |
| `RoutinesScheduledJobsRegistrar` | 0 | → `TEST-14` |
| `RoutineTodoListExtensions` | indirect | Via display-order tests |

### Honest coverage number

**Roughly 60% of the module is meaningfully covered — but that number is lopsided.** The domain core
(the two static services, the two jobs, the freeze endpoint, the grouped read) is at ~90% and is
genuinely well-pinned. The CRUD surface is at ~20%: eleven of twenty-three endpoints have no test at
all, and the ones that are missing are precisely the plain-looking ones that turned out to carry
`SEC-1` (no ownership check on `TimePeriodId`), `SEC-2` (a `PUT` that flips `IsDone` without the
cross-slice event) and `CQ-5` (read endpoints serving a malformed period shape).

**The riskiest gaps, in order:** (1) the create/update ownership hole, which is a live cross-tenant
write and has no test on any path; (2) the `ComputeNextReset` catch-up loop, where the existing tests
are shaped so as to be structurally incapable of catching a total failure of monthly and yearly
resets; (3) the activity-merge collision, which will throw a 500 on a user-facing operation and whose
existing test seeds exactly one routine, one short of the failing case.

## 12. Missing tests — backlog

| ID | Gap | Prompt file |
|---|---|---|
| `TEST-1` | `ComputeNextReset` never tested with `now` past the computed candidate (hides `CQ-1`) | `testingPrompts/ComputeNextResetCatchUp.md` |
| `TEST-2` | Activity merge with two routines colliding on `(UserId, TimePeriodId, ActivityId)` | `testingPrompts/ActivityMergeRoutineCollision.md` |
| `TEST-3` | Create/Update routine item with a foreign or non-existent `TimePeriodId` | `testingPrompts/RoutineTodoListCrud.md` |
| `TEST-4` | Range tests cannot distinguish FluentValidation from a DB check violation | `testingPrompts/ValidatorVsDbConstraint.md` |
| `TEST-5` | `PUT /routine-todo-list` flipping `IsDone` — event fan-out and streak side-effects | `testingPrompts/RoutineTodoListCrud.md` |
| `TEST-6` | Read endpoints' response shape (`NextResetAt`, freeze fields) | `testingPrompts/RoutineReadEndpointShape.md` |
| `TEST-7` | Concurrent reset writing a duplicate `RoutinePeriodCompletion` | `testingPrompts/ConcurrentResetRace.md` |
| `TEST-8` | Untick/retick inflating the item streak; step-toggle reset path | `testingPrompts/ToggleIsDoneStreak.md` |
| `TEST-9` | Unauthenticated / wrong-role access across the untested endpoints | `testingPrompts/RoutineAuthMatrix.md` |
| `TEST-10` | `TryReset_Single_...UpdatesPeriod` asserts a property the method never sets | `testingPrompts/ValidatorVsDbConstraint.md` (§2) |
| `TEST-11` | `PUT /routine-time-period` — freeze-budget clamp on lowering the budget | `testingPrompts/RoutineTimePeriodUpdate.md` |
| `TEST-12` | `DELETE` on both entities — FK `Restrict` behaviour with children present | `testingPrompts/RoutineDeleteBehaviour.md` |
| `TEST-13` | `RoutineTodoListSeeder` (dev) — truncate scope and per-user seeding | `testingPrompts/RoutineDevSeeder.md` |
| `TEST-14` | `RoutinesScheduledJobsRegistrar` present in the composition root | `testingPrompts/ScheduledJobsRegistrarWiring.md` |
