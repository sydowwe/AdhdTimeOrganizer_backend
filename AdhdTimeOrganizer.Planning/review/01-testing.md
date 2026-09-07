# AdhdTimeOrganizer.Planning — Review 01: Testing

> Point-in-time snapshot, 2026-08-31. Disposable. The durable map is `docs/domain-map.md`;
> the durable test strategy is `docs/testing.md`. This file records what is and is not covered
> **today**, and mints the `TEST-n` ids that files 02 and 03 reference.

## Test infrastructure summary

Planning has no test project of its own — everything lives in `AdhdTimeOrganizer.IntegrationTests`,
split three ways exactly as `docs/testing.md` describes: `Services/` (plain xUnit over the two pure
domain services), `Endpoints/` (Postgres) and `Reminders/` (Postgres + the `Sydowwe.Reminders` module
surface). Infrastructure comes from `Sydowwe.Framework.Testing`: `PostgresTestBase` over the
`"Postgres"` collection with a real container behind `AppDbContextFixture`, and
`UserRoleTestAuthHandler` / `FakeLoggedUserService` for role and ownership scenarios. Two bespoke
helpers carry the seeding: `PlanningTestSeedHelper` (activity / calendar / planner task / importance /
day template / template line / repeating task) and `ReminderSeedHelper` (the portal row *and* the
module-side `ReminderDefinition`).

Nothing here is fragile in the flaky sense — there are no `Thread.Sleep`s, no wall-clock races, and the
two pure services take `today` as a parameter rather than reading the clock. Two structural notes:

- **`CompletionFanOutTests` seeds through `SeedAsync(DbContext)` and shares one `TaskPriority` across
  every test in the class.** That is the only cross-test shared state in the Planning surface and it is
  read-only, so it is fine — but it is the one place where adding a mutating test would couple two
  cases together.
- **Several suites reach past Planning into TodoLists and the Reminders module by design**
  (`CompletionFanOutTests`, `PlanningRouteSmokeTests`, the whole `Reminders/` folder). That is correct —
  the seams are the thing under test — but it means a Planning-only change can go red in a file whose
  name says nothing about Planning.

## 7. Test quality assessment

**Strengths — this is a well-tested slice, and unusually honest about it.**

- **Assertions are on rows, not on routes.** `CalendarBatchEndpointTests.BatchDayType_WritesOnlyTheDayType`
  re-reads the calendar and asserts the *other* columns are unchanged; `DayPlanEndpointTests.
  DayPlan_CarriesTheStreakAtTheTopLevel_AndNotAlsoOnTheCalendar` asserts the nested copy is null rather
  than just that the top-level one is present. Both would pass on a `200` check alone.
- **Every batched or cross-day surface really does hand the endpoint a second user's id.**
  `CopyToDays_RejectsAnotherUsersSourceDay`, `ApplyTemplateBatch_AppliesPerDay_AndFailsOnlyTheUnreachableDay`,
  `Trend_AnotherUsersTasks_AreNeverCounted`, `DayPlan_NeverReachesAnotherUsersDay`,
  `PinningSomeoneElsesTemplate_IsNotFound`, `TaskSummaries_GroupsRangeByDay_OmittingEmptyDaysAndOtherUsers`.
  The single-user version of each of these would pass while a lost query filter served other people's days.
- **The reversibility pin is the right shape.** `PlannerStreakServiceTests.Walk_UntickingAndRetickingATask_IsExactlyReversible`
  and its HTTP counterpart `Streak_UntickingATask_LowersIt_AndRetickingRestoresItExactly` pin the *reason*
  the streak is derived rather than stored, not just its current value.
- **Tests document findings instead of hiding them.** `PlannerTaskStatusAndSpanTests.
  CreatingAnOvernightTask_IsAcceptedWithNegativeDuration` asserted `Created` and explained in its own body
  that this was wrong and which assertion to flip when it was fixed — which is exactly how it was closed on
  2026-09-08: the rule landed (`CQ-14`) and the same test, renamed `CreatingAnOvernightTask_IsRejected`,
  now pins the opposite plus the `PUT` path it never covered. `CompletionFanOutTests` opens with a
  four-item list of points where its originating prompt was stale, each re-confirmed against source.
  That is the opposite of test theater.
- **The pure-service suites cover the branches that matter** and stay out of Postgres: 17 tests on
  `PlannerStreakService`, 12 on `PlanVsActualTrendService`, including both midnight-wrap cases and the
  completed-but-unmeasurable case that is the trap in the trend response.

**Weaknesses.**

- **`PlanningRouteSmokeTests.PlanningRoutes_AreRegistered` asserts only `NotBe(NotFound)`** — a 500 passes.
  That is defensible for a wiring smoke test, but four routes is a thin sample of ~55, and the two
  hand-written suggestion routes (the ones most likely to 500 on a stale materialized view) are not among
  them.
- **`PlannerDeleteBehaviorTests` display names are now wrong.** Two of the five say
  *"contradicting docs/domain-map.md"* — but the rewritten `domain-map.md` states exactly the behaviour
  those tests assert. A reader hitting a failure there will chase a contradiction that no longer exists.
  → `DOC-3` in file 02.
- **The auth matrix is broad but shallow.** `PlanningCrudAuthMatrixTests` wires 23 endpoints into the
  Framework base suites in one file, which is excellent leverage — but the base suites test the *base*
  behaviour. Nothing in the matrix exercises a Planning-specific validator, so a validator that is never
  wired up (`CQ-8`) is invisible to it.
- **No test asserts a validator rejects anything on the create/update planner-task path** except the
  exactly-one-of-`CalendarId`/`Date` rule. `PlannerTaskValidator` has ten rules; one is covered.
- **`Trend_WithUntilBeforeFrom_IsRejected` and `TaskSummaries_RejectsInvertedRange` test the inverted
  range but never the over-length one**, so both `PlannerBatchLimits` ceilings are unpinned at their
  boundary — already noted as a known gap in `docs/testing.md`, and still true.

## 8. Coverage matrix

Legend: ✅ covered · ⚠ partial · ❌ absent. "Auth" = at least one test proving another user's / an
anonymous caller's request does not succeed.

### Calendar (`application/endpoint/calendar/`)

| Endpoint | Happy | Edge | Auth | Test file |
|---|---|---|---|---|
| `GetByIdCalendarEndpoint` | ✅ | ✅ | ✅ | `PlanningCrudAuthMatrixTests` |
| `GetByDateCalendarEndpoint` | ✅ | ❌ `TEST-1` | ❌ `TEST-1` | `PlannerStreakTests` (indirect) |
| `GetDayPlanCalendarEndpoint` | ✅ (6) | ✅ | ✅ | `DayPlanEndpointTests` |
| `FilterCalendarEndpoint` | ❌ `TEST-2` | ❌ `TEST-2` | ❌ `TEST-2` | — |
| `GetTaskSummariesCalendarEndpoint` | ✅ | ⚠ inverted only, `TEST-3` | ✅ | `CalendarBatchEndpointTests` (2) |
| `UpdateCalendarEndpoint` | ✅ | ❌ day-type 400, `TEST-4` | ✅ | `PlanningCrudAuthMatrixTests` |
| `BatchUpdateDayTypeCalendarEndpoint` | ✅ | ✅ | ✅ | `CalendarBatchEndpointTests` (2) |

### Planner tasks (`application/endpoint/plannerTask/`)

| Endpoint | Happy | Edge | Auth | Test file |
|---|---|---|---|---|
| `CreatePlannerTaskEndpoint` | ✅ | ⚠ `TEST-5` | ✅ | `LazyCalendarCreationTests` (4), `PlannerTaskStatusAndSpanTests` |
| `UpdatePlannerTaskEndpoint` | ✅ | ✅ | ✅ | `PlanningCrudAuthMatrixTests`, `LazyCalendarCreationTests` |
| `DeletePlannerTaskEndpoint` | ✅ | ✅ | ✅ | `PlannerDeleteBehaviorTests`, `ReminderRegistrationTests` |
| `BatchDeletePlannerTaskEndpoint` | ❌ `TEST-6` | ❌ `TEST-6` | ❌ `TEST-6` | — |
| `PatchPlannerTaskSpanEndpoint` | ✅ | ✅ | ✅ | `PlannerTaskStatusAndSpanTests`, `ReminderRegistrationTests` |
| `PatchPlannerTaskStatusEndpoint` | ✅ | ✅ | ⚠ | `PlannerTaskStatusAndSpanTests`, `PlannerStreakTests`, `CompletionFanOutTests` |
| `CopyToDaysPlannerTaskEndpoint` | ✅ | ✅ | ✅ | `CalendarBatchEndpointTests` (2) |
| `ApplyTemplatePlannerTaskEndpoint` | ✅ (4) | ⚠ `TEST-7` | ❌ `TEST-7` | `ApplyTemplatePlannerTaskConflictResolutionTests` |
| `ApplyTemplateBatchPlannerTaskEndpoint` | ✅ | ✅ | ✅ | `CalendarBatchEndpointTests` (2) |
| `GetByIdPlannerTaskEndpoint` | ✅ | ✅ | ✅ | `PlanningCrudAuthMatrixTests` |
| `FilterPlannerTaskEndpoint` | ❌ `TEST-8` | ❌ `TEST-8` | ❌ `TEST-8` | — |
| `FormSelectOptionsPlannerTaskEndpoint` | ❌ `TEST-9` | ❌ | ❌ | — |
| `GetPlanVsActualTrendPlannerTaskEndpoint` | ✅ (8) | ✅ | ✅ | `PlanVsActualTrendTests` |

### Reminders (`application/endpoint/reminder/`)

| Endpoint | Happy | Edge | Auth | Test file |
|---|---|---|---|---|
| `CreateReminderEndpoint` | ✅ | ✅ | ✅ | `ReminderEndpointTests`, `ReminderRegistrationTests` (16) |
| `UpdateReminderEndpoint` | ✅ | ✅ | ✅ | as above |
| `DeleteReminderEndpoint` | ✅ | ✅ | ✅ | as above |
| `GetByIdReminderEndpoint` | ✅ | ✅ | ✅ | `ReminderEndpointTests` |
| `GetByDateReminderEndpoint` | ✅ | ❌ recurrence expansion, `TEST-10` | ✅ | `ReminderCrudAuthMatrixTests` (3) |

The reminder surface is the best-covered part of the slice. The one hole is the *day-view recurrence
maths* (`OccursOn`, month-end and 29-Feb semantics, the DST nudge in `ToUtc`) — none of which is reached
by the three by-date tests.

### Repeating tasks, template lines, day templates, importance, settings

| Endpoint | Happy | Edge | Auth | Test file |
|---|---|---|---|---|
| `Create/Update/Delete/GetByIdRepeatingPlannerTaskEndpoint` | ✅ | ❌ recurrence rules, `TEST-11` | ✅ | `PlanningCrudAuthMatrixTests` |
| `GetAllRepeatingPlannerTaskEndpoint` | ❌ `TEST-12` | ❌ | ❌ | — |
| `GetSuggestionsRepeatingPlannerTaskEndpoint` | ✅ (5) | ⚠ stale-view NRE `TEST-13` | ❌ | `RepeatingPlannerTaskSuggestionsTests` |
| `Create/Update/Delete/BatchDelete/GetByIdTemplatePlannerTaskEndpoint` | ✅ | ✅ | ✅ | `PlanningCrudAuthMatrixTests` |
| `TemplatePlannerTaskChangeSpanEndpoint` | ❌ `TEST-14` | ❌ | ❌ | — |
| `FilterTemplatePlannerTaskEndpoint` | ❌ `TEST-8` | ❌ | ❌ | — |
| `Create/Update/Delete/GetByIdTaskPlannerDayTemplateEndpoint` | ✅ | ✅ | ✅ | `PlanningCrudAuthMatrixTests` |
| `GetAllTaskPlannerDayTemplateEndpoint` | ✅ | ✅ | ✅ | `TaskPlannerDayTemplatePinTests` |
| `GetSelectOptionsTaskPlannerDayTemplateEndpoint` | ⚠ smoke only | ❌ | ❌ | `PlanningRouteSmokeTests` |
| `SetPinnedTaskPlannerDayTemplateEndpoint` | ✅ (6) | ✅ | ✅ | `TaskPlannerDayTemplatePinTests` |
| `GetSuggestionsTaskPlannerDayTemplateEndpoint` | ❌ `TEST-13` | ❌ | ❌ | — (known gap in `docs/testing.md`) |
| `Create/Update/Delete/GetById/GetAllTaskImportanceEndpoint` | ✅ | ✅ (409 reorder) | ✅ | `PlanningCrudAuthMatrixTests`, `TaskImportanceReorderTests` (3) |
| `GridTaskImportanceEndpoint` | ❌ `TEST-15` | ❌ | ❌ | — |
| `GetSelectOptionsTaskImportanceEndpoint` | ❌ `TEST-15` | ❌ | ❌ | — |
| `GetPlannerSettingsEndpoint` | ✅ | ❌ concurrent, `TEST-16` | ❌ | `UserPlannerSettingsTests` (4) |
| `UpdatePlannerSettingsEndpoint` | ✅ | ⚠ | ❌ | `UserPlannerSettingsTests` |

### Services, helpers, seams, handlers, seeders

| Unit | Tests | Notes |
|---|---|---|
| `PlannerStreakService` | 17 | Every rule; the reversibility and three-leniency pins are load-bearing |
| `PlanVsActualTrendService` | 12 | Both midnight-wrap paths; the unmeasurable-completed case |
| `PlannerStreakReader` | 8 (via HTTP) | `PlannerStreakTests` covers the predicate half |
| `PlanVsActualTrend` eligibility predicate | 8 (via HTTP) | `PlanVsActualTrendTests` |
| `PlannerTemplateApplier` | 5 | All four conflict modes, plus `TEST-17` (2026-09-08): a negative-duration task is left whole instead of carved away, with an ordinary task through the same blocker as the control |
| `CalendarProvisioner` | 7 (via HTTP) | Race branch not exercised concurrently → `TEST-18` (already a documented gap) |
| `CalendarDayFactory` | ✅ theory | `ALazilyCreatedDay_CarriesTheSameMetadataTheSeederWouldHaveGivenIt` |
| `HolidayCalendar` | **0** | Computus and both country tables entirely untested → `TEST-19` |
| `CalendarDayTypeRule` | 2 | Both failure shapes (400 and per-day `succeeded:false`) |
| `TaskPlannerHelper.TasksOverlap` | 0 direct | Exercised only through the applier |
| `ReminderRegistrationService` | 16 | Key format, cancel-vs-register, defaults, DST composition untested → `TEST-10` |
| `PlannerTaskActivityReferenceSource` | ✅ | `SeamWiringTests`, `ActivityMergeTests`, `ActivityForeignKeyInventoryTests` |
| `CalendarDayLookup` | ⚠ indirect | Only through History's calendar dashboard tests → `TEST-20` |
| `TodoListItemIsDoneChangedEventHandler` | 4 | `CompletionFanOutTests` (CQ-7 block) |
| `RoutineTodoListIsDoneChangedEventHandler` | 6 | `RoutineCompletionFanOutTests` (`TEST-21`, closed 2026-09-08 — it had been named in the `CompletionFanOutTests` class doc but never exercised, which is where `CQ-1`…`CQ-4` came from) |
| `TaskImportanceSeeder` / `UserPlannerSettingsSeeder` | ✅ | `PerUserDefaultSeederTests`, `PerUserDefaultMatcherTests` |
| `CalendarSeeder` | ⚠ indirect | Its hand-rolled `SetupDefaults` / no-op `ResetDefaults` unpinned → `TEST-22` |
| `TaskPlannerDayTemplateSeeder` / `TemplatePlannerTaskSeeder` (dev) | **0** | `TruncateTable`'s cascade blast radius untested → `TEST-23` |

### The one honest number

**≈ 72% ship-confidence.** Every derived number, every batched entry point, the whole reminder seam and
the four conflict-resolution modes are covered behaviourally, with a second user, asserting on rows —
that is the hard 70%. The missing quarter is concentrated in three places, and they are not evenly risky:

1. **Read surfaces that nothing tests at all** — `FilterCalendarEndpoint`, `FilterPlannerTaskEndpoint`,
   `FilterTemplatePlannerTaskEndpoint`, `GridTaskImportanceEndpoint`, the two `GetSelectOptions` routes,
   `GetAllRepeatingPlannerTaskEndpoint`. `FilterPlannerTaskEndpoint` is the day-planner view's *only*
   read and carries a non-trivial midnight-wrap predicate duplicated verbatim in a second file
   (`CQ-12`); it is the single riskiest untested thing in the slice.
2. ~~**`RoutineTodoListIsDoneChangedEventHandler`** — zero tests, and file 02 finds four defects in its
   45 lines (`CQ-1`…`CQ-4`). Its sibling handler, which *is* tested, has none of them. That correlation
   is the argument for `TEST-21`.~~ **Closed 2026-09-08.** `RoutineCompletionFanOutTests` covers it and
   all four defects are fixed; the correlation held exactly as stated, which is the argument for
   treating the remaining zero-coverage rows in the table above as findings-in-waiting rather than as
   merely untested.
3. **Validator rules generally** — three of the recurrence/ordering rule sets are never exercised, and
   one validator may not be wired at all (`CQ-8`).

## 12. Missing tests — backlog

Paste-ready prompts under `review/testingPrompts/`. Read `_SHARED-CONTEXT.md` first — it carries the
fixture, auth and seeding conventions every prompt assumes.

| ID | Gap | Prompt |
|---|---|---|
| `TEST-1` | `GetByDateCalendarEndpoint`: bad format, absent date 404, cross-user 404 | `ReadSurfaceSweep.md` |
| `TEST-2` | `FilterCalendarEndpoint`: range, sort, cross-user, unbounded range | `ReadSurfaceSweep.md` |
| `TEST-3` | Both `PlannerBatchLimits` ceilings at their boundary (366 / 367) | `BatchLimitBoundaries.md` |
| `TEST-4` | `UpdateCalendarEndpoint` day-type violation is a 400 | `CalendarDayTypeRuleBothShapes.md` |
| `TEST-5` | `PlannerTaskValidator`'s ten rules. **Cases 15/16 (the ordering rule) are done** — the rule landed 2026-09-08 and `CreatingAnOvernightTask_IsRejected` covers create, update and equal-times; the other cases remain open | `PlannerTaskValidatorRules.md` |
| `TEST-5`(b) | ✅ **Written 2026-09-08** with the `SEC-1` fix — `PlannerReferencedIdOwnershipTests` (10) covers all four ids on create, update, both apply-template routes and planner settings, each with a control. Still open for `TemplatePlannerTaskRequest` / `RepeatingPlannerTaskRequest`, which inherit the same `ActivityId` / `ImportanceId` | `ForeignKeyOwnership.md` |
| `TEST-6` | `BatchDeletePlannerTaskEndpoint` + its reminder cancellation | `BatchDeletePlannerTask.md` |
| `TEST-7` | `ApplyTemplatePlannerTaskEndpoint`: cross-user calendar/template, error mapping | `ApplyTemplateSingleDayEdges.md` |
| `TEST-8` | The duplicated midnight-wrap window predicate, both filter endpoints | `PlannerTaskFilterWindow.md` |
| `TEST-9` | `FormSelectOptionsPlannerTaskEndpoint` scoping | `ReadSurfaceSweep.md` |
| `TEST-10` | Reminder day-view recurrence expansion + DST composition | `ReminderRecurrenceAndDst.md` |
| `TEST-11` | `RepeatingPlannerTaskValidator`'s four recurrence shapes | `RepeatingPlannerTaskValidatorWiring.md` |
| `TEST-12` | `GetAllRepeatingPlannerTaskEndpoint` scoping | `ReadSurfaceSweep.md` |
| `TEST-13` | Both suggestion endpoints against a **stale** materialized view | `StaleSuggestionView.md` |
| `TEST-14` | `TemplatePlannerTaskChangeSpanEndpoint` | `ReadSurfaceSweep.md` |
| `TEST-15` | `GridTaskImportanceEndpoint` + both `GetSelectOptions` routes | `ReadSurfaceSweep.md` |
| `TEST-16` | Concurrent `GET /planner/settings` must not make two rows | `SettingsGetOrCreateRace.md` |
| `TEST-17` | ✅ **Written 2026-09-08** — carving an **overnight** existing task must not silently delete it. `ApplyTemplatePlannerTaskConflictResolutionTests.MergeOverwrite_LeavesANegativeDurationTaskWhole_InsteadOfDeletingIt` | `CarveOvernightTask.md` |
| `TEST-18` | `CalendarProvisioner` race branch under two concurrent writers | `ConcurrentCalendarProvisioning.md` |
| `TEST-19` | `HolidayCalendar`: Computus, both countries, leap years | `HolidayCalendarTable.md` |
| `TEST-20` | `CalendarDayLookup` directly, incl. cross-user | `ReadSurfaceSweep.md` |
| ~~`TEST-21`~~ | ~~`RoutineTodoListIsDoneChangedEventHandler` — the whole handler~~ ✅ done 2026-09-08 → `Endpoints/RoutineCompletionFanOutTests.cs` (6 tests; scenario F deferred with `CQ-5`) | `RoutineTodoListFanOut.md` |
| `TEST-22` | `CalendarSeeder`'s hand-rolled setup / no-op reset | `CalendarSeederPartialYear.md` |
| `TEST-23` | Dev seeder `TRUNCATE … CASCADE` blast radius | `DevSeederTruncateBlastRadius.md` |
