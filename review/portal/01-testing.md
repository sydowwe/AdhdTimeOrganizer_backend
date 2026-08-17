# Portal review — 01 · Testing

> ⚠ **Scope warning.** This review covers **42 of 712** portal source files (~6%). The fan-out was
> halted by an API session limit before reaching `application/endpoint/**` (275 files),
> `application/validator/**` (62), `application/dto/**` (198), and most of
> `infrastructure/persistence/configuration/**` and `seeder/**`. The coverage matrix below is
> therefore **not** a portal-wide endpoint matrix — it is a matrix over the reviewed surface plus
> what the test project itself reveals. See `00-STATUS.md` and `REMAINING.txt`.

## Test infrastructure summary

> **Update (2026-08-17):** a large batch of new test files landed since the pass this doc was
> written from — 25 new files across `Endpoints/`, `Infrastructure/`, `Routines/` and `Seeding/`.
> The project now holds **165 concrete test classes and ~390 `[Fact]`/`[Theory]` methods across 71
> files** (was 10 classes / 104 facts). Most of the backlog in §12 below is now resolved; see the
> per-item status there. The narrative below is updated in place rather than left as a stale
> baseline.

The portal has one integration-test project, `AdhdTimeOrganizer.IntegrationTests`. It follows the
documented framework pattern exactly: the real portal `Program` runs against a
`Testcontainers.PostgreSql` container via `Sydowwe.Framework.Testing`'s
`PostgresContainerFixture<TProgram, TDbContext>`, closed here by
`Infrastructure/AppDbContextFixture.cs`, with `RoleTestAuthHandler` (scheme `"Test"`) swapping auth
and `TestWebApplicationFactory<TProgram>` building role-parametrized clients. Tests are tagged
`[Collection("Postgres")]`; xunit v3 + FluentAssertions + Moq + Respawn.

The abstract framework CRUD/auth test bases (`Base{Create,Update,Delete,GetById,GetAll,
GetSelectOptions,BatchDelete,Patch,Filter,FilterSort,FetchTable,ToggleIsHidden}EndpointTests`, 13 in
total) live in the `Sydowwe.Framework.Testing` submodule, not in the portal — the portal's own
`Endpoints/Base*EndpointTests.cs` files are misnamed in the earlier version of this doc; they are
**concrete** per-entity pins (e.g. `CreateActivityCategoryEndpointTests`), not the abstract bases
themselves. **Correction to the previous finding:** "nothing in the portal subclasses the framework
bases" is no longer true — dozens of new concrete classes now do, spanning Activity/lookups,
Activity*Profile grids, PlannerTask/Calendar/DayTemplate/TemplatePlannerTask, and the TodoList family.
The auth matrix and 404 paths those bases ship are now exercised across most of the reviewed CRUD
surface.

Two pieces are bespoke and worth watching:

- `AppDbContextFixture.OnSchemaCreatedAsync` applies
  `infrastructure/persistence/sqlScripts/*.sql` (the three suggestion-pattern materialized views),
  copied next to the test binaries by a `Content` item. `EnsureCreated` skips them, and without them
  `SuggestionPatternRefreshInterceptor` throws 42P01 on any save touching `PlannerTask`,
  `ActivityHistory` or `Calendar`. This is a **second, parallel implementation** of what
  `SuggestionPatternViewInstaller` does at runtime (embedded resources + `to_regclass`). Both read
  the same three files, but the two code paths can drift — see `MIG-3` in `03-risks-rollout.md`.
- `Seeding/PerUserDefaultMatcherTests.cs` is a pure unit test (no DB) pinning `PerUserDefaultMatcher`.
  Per CLAUDE.md this exists specifically to keep two shipped 23505 bugs dead (count-based seeder
  guards, positional resets). It is the right shape and should be the model for more of this suite.

**No `[Trait("Status","KnownGap")]` tags exist anywhere in the project.** There is no prior review
baseline and no pinned-known-gap convention in use yet, so nothing below is a re-flag.

## 7. Test quality assessment

I did not read the test bodies — this assessment is inferred from file inventory, naming, and the
production-code fragments that named their own test coverage. Treat confidence as moderate.

**Strengths.**

- **Auth is genuinely covered, and covered at the right level.** Three dedicated classes
  (`AuthFunctionalTests`, `AuthSecurityTests`, `RegistrationTests`) against the real pipeline.
  CLAUDE.md cites `AuthFunctionalTests.Logout_RevokesRefreshToken_WhenAccessTokenIsExpired` as the
  test pinning `BaseLogoutEndpoint`'s deliberate `AllowAnonymous()` — that is exactly the kind of
  "pins a decision that looks like a bug" test that earns its keep.
- **Composition is pinned.** `Modules/ModuleWiringTests.cs` covers the wiring rules that "none of
  which break the build" — the double-registration `Except`, the `DbContext` alias, the
  `IQuietHoursReader` resolution. The `DependencyInjectionExtensions` and `ModuleServiceExtensions`
  fragments both came back essentially clean, which is consistent with that test doing its job.
- **Domain logic is unit-tested where it is pure.** `RoutineResetServiceTests` exists and
  `RoutineResetService` is genuinely pure (no DB access), so these can be fast, deterministic and
  exhaustive. Same for `PerUserDefaultMatcherTests`.
- **Test seeding has a shared helper** (`Reminders/ReminderSeedHelper.cs`) rather than
  copy-pasted arrange blocks.

**Weaknesses (updated 2026-08-17 — several of these are now resolved or narrowed; superseded text
struck through in spirit, kept visible for context).**

- ~~The 13 abstract endpoint test bases are unused.~~ **Resolved.** New concrete classes
  (`ActivityEndpointTests.cs` — 37 subclasses; `PlanningCrudAuthMatrixTests.cs` — 26 subclasses;
  `TodoListCrudAuthMatrixTests.cs` — 34 subclasses) now exercise the framework auth matrix across
  Activity + its 4 lookups + the 3 `Activity*Profile` grids, Planning (PlannerTask, Calendar,
  TaskImportance, RepeatingPlannerTask, DayTemplate, TemplatePlannerTask), and the TodoList family.
  That closes the a-priori highest IDOR risk called out below (`TEST-5`, the unfiltered hand-scoped
  `Activity*Profile` grids) and most of `TEST-3`/`TEST-4`. Timer presets (`TEST-1`), Reminders
  (`TEST-2`, already partially covered before this batch), History (`TEST-6`), Tracking (`TEST-7`),
  export/deletion (`TEST-9`) and Timer CRUD (`TEST-10`) are **still** unsubclassed — the risk is
  narrowed, not eliminated.
- **Coverage is concentrated where the code is already safest — still true for the logging pipeline
  and tracking ledgers, no longer true for the view-refresh interceptor or the routine jobs.** Auth,
  wiring and pure domain math were already well covered. The view-refresh interceptor now has
  `Infrastructure/SuggestionPatternRefreshTests.cs` (9 facts, incl. failure-injection — see below),
  and both routine jobs now have dedicated integration tests (see next point). The Serilog/logging
  pipeline (`SEC-1`) and the tracking ledgers (`SEC-4`/`SEC-5`) remain **untested**.
- ~~Two shipped defects prove the event-handler paths are untested.~~ **Resolved for the fan-out
  handlers; the underlying defects need re-verification.** `Endpoints/CompletionFanOutTests.cs` (9
  facts) now covers the 3 completion-fan-out event handlers, including a concurrency case. Per the
  new test file's own comments, `CQ-6`/`CQ-7` no longer reproduce and the `CQ-8` dead events
  (`ActivityAddedToHistoryEvent`, `ActivityCreatedIsOnTodoListEvent`) are not present in current code
  — `02-findings.md` should be re-checked for staleness on those three IDs, out of scope for this
  file. Likewise `RoutineTodoListResetJobHandlerTests.cs` now has
  `ResetSweep_UnticksTheItemAndAllOfItsSteps`, directly pinning the `CQ-3` behavior via the job's
  actual `Include` chain rather than the pure service.
- ~~Assertion theater risk is concentrated in the job/notification tests.~~ **Resolved for the two
  routine jobs and the view-refresh interceptor.** `Routines/RoutinePeriodNudgeJobTests.cs` (12
  facts) includes `NudgeSweep_MidLoopNotifierFailure_PreservesEarlierMarkersAndStillProcessesLaterPeriods`
  — the exact `CQ-5` failure-injection case this doc previously flagged as missing.
  `RoutineTodoListResetJobHandlerTests.cs` has the equivalent
  `ResetSweep_PersistsTheReset_EvenWhenTheNotifierThrows`, and `SuggestionPatternRefreshTests.cs` has
  `FailedSave_DoesNotLeaveFlagsPrimedForTheNextSave` (`CQ-10`) and a partial-refresh-failure case
  (`CQ-9`). Happy-path-only risk remains for everything outside these three components.
- **No concurrency coverage → now one test, still thin.** `row_version` is on every table via
  `BaseEntityConfigure`; `CompletionFanOutTests.cs` adds a single concurrent-patch test, but the
  `CQ-9`/`CQ-11`/`TodoListItemIsDoneChanged` unhandled-`DbUpdateConcurrencyException` concern is
  otherwise still untested across the rest of the row_version surface.

## 8. Coverage matrix

### Endpoints

> Updated 2026-08-17 after the new test batch. The unreviewed-surface caveat from the top of this
> doc still applies to areas marked ❌ below — those are genuinely untested, not just unreviewed.

| Endpoint area (domain-map) | Happy | Edge | Auth | Test file | ID |
|---|---|---|---|---|---|
| Auth / registration / sessions | ✅ | ✅ | ✅ | `Auth/*` (3 files) | — |
| Extension activity-tracking ingest | ✅ | ⚠ partial | ✅ | `Endpoints/ExtensionActivityTrackingTests.cs` | — |
| Timer preset validation | ✅ | ✅ | ❌ | `Endpoints/TimerPresetValidationTests.cs` | `TEST-1` |
| Reminder CRUD + day view | ✅ | ⚠ partial | ❌ | `Reminders/ReminderEndpointTests.cs` | `TEST-2` |
| **Activity planning** (calendar, planner tasks, templates, suggestions) | ✅ | ✅ | ✅ | `Endpoints/PlanningCrudAuthMatrixTests.cs` (26 subclasses) + `ApplyTemplatePlannerTaskConflictResolutionTests.cs`, `PlannerTaskStatusAndSpanTests.cs`, `RepeatingPlannerTaskSuggestionsTests.cs`, `PlannerDeleteBehaviorTests.cs`, `UserPlannerSettingsTests.cs` | `TEST-3` ✅ resolved |
| **To-do lists** (lists, items, steps, priorities, routine lists) | ✅ | ✅ | ✅ | `Endpoints/TodoListCrudAuthMatrixTests.cs` (34 subclasses) + `TodoListDeleteBehaviorTests.cs`, `TodoListStepsCrudTests.cs`, `TodoListToggleAndDoneCountTests.cs`, `TaskPriorityReorderTests.cs`, `TaskImportanceReorderTests.cs`, plus 4 routine-period-scoped files | `TEST-4` ✅ resolved |
| **Activity** (activity, roles, categories, 4 lookups, anchors, 3 profiles) | ✅ | ✅ | ✅ | `Endpoints/ActivityEndpointTests.cs` (37 subclasses) | `TEST-5` ✅ resolved |
| **Activity history + dashboards** | ❌ | ❌ | ❌ | *none* | `TEST-6` |
| **Desktop / Android tracking + dashboards** | ❌ | ❌ | ❌ | *none* | `TEST-7` |
| **Google Calendar endpoints** | ⚠ partial | ❌ | ❌ | `Endpoints/SyncCalendarToGoogleTests.cs` (3 facts) | `TEST-8` ⚠ partial |
| **User data export / account deletion** | ❌ | ❌ | ❌ | *none* | `TEST-9` |
| Timers (`TimerPreset`, `PomodoroTimerPreset`) CRUD | ❌ | ❌ | ❌ | *none* | `TEST-10` |

`TEST-5` — the three `Activity*Profile` grids — is now covered: per `domain-map.md` those entities
are **not** `IEntityWithUser`, have **no** global query filter, and scope by hand in
`ApplyCustomFiltering`; `ActivityEndpointTests.cs` now exercises that hand-rolled scoping path through
the framework auth-matrix bases, closing what was previously the highest a-priori IDOR risk in the
portal. Note: `PlannerDeleteBehaviorTests.cs` surfaced two behavior/doc contradictions while adding
this coverage (Calendar delete is `Restrict`, not `Cascade`; Activity delete `Cascade`s to
PlannerTask, contradicting `domain-map.md`) — those read as new findings for `02-findings.md`, not
addressed here since that file is out of scope for this pass.

### Services, jobs, handlers, seeders (reviewed surface only)

| Component | Tests | ID |
|---|---|---|
| `RoutineResetService` (pure domain) | ✅ `Services/RoutineResetServiceTests.cs` | — |
| `RefreshTokenService` | ✅ `Services/RefreshTokenServiceTests.cs` | — |
| `PerUserDefaultMatcher` | ✅ `Seeding/PerUserDefaultMatcherTests.cs` | — |
| `ReminderRegistrationService` | ✅ `Reminders/ReminderRegistrationTests.cs` | — |
| Routine notifications | ⚠ `Routines/RoutineNotificationTests.cs` — failure path unproven | `TEST-11` |
| Module wiring / composition | ✅ `Modules/ModuleWiringTests.cs` | — |
| `RoutineTodoListResetJob` | ✅ `Routines/RoutineTodoListResetJobHandlerTests.cs` (10 facts) — covers `CQ-2` grace persistence, `CQ-3` step unticking via the real `Include` chain, double-fire idempotency, and notifier-throws failure injection | `TEST-12` ✅ resolved |
| `RoutinePeriodNudgeJob` | ✅ `Routines/RoutinePeriodNudgeJobTests.cs` (12 facts) — includes the `CQ-5` mid-loop notifier-failure case | `TEST-13` ✅ resolved |
| `SuggestionPatternRefreshInterceptor` | ✅ `Infrastructure/SuggestionPatternRefreshTests.cs` (9 facts) — covers `CQ-9` (one view's refresh failing doesn't block others) and `CQ-10` (failed save doesn't leave flags primed); `PERF-1` (query cost) is not a correctness test and remains unaddressed | `TEST-14` ✅ mostly resolved |
| `SuggestionPatternViewInstaller` | ⚠ `SuggestionPatternRefreshTests.cs` adds a resource/script consistency check, but the fixture still reimplements view creation in parallel rather than calling the installer directly | `TEST-15` ⚠ partial |
| 5 event handlers (completion fan-out) | ✅ `Endpoints/CompletionFanOutTests.cs` (9 facts, incl. 1 concurrency case) — per the test file's own findings, `CQ-6`/`CQ-7` no longer reproduce on current code and `CQ-8`'s two "dead" events aren't present in the repo; re-verify those 3 IDs against `02-findings.md` (out of scope here) | `TEST-16` ✅ resolved |
| `DefaultUsersSeeder` | ✅ `Seeding/DefaultUsersSeederTests.cs` (12 facts) — covers `CQ-1` (failed `CreateAsync` doesn't partially create admin) plus a PII-logging pin (raw email never logged on duplicate-email failure) | `TEST-17` ✅ resolved |
| 12 per-user default seeders | ⚠ matcher only, no seeder-level test | `TEST-18` |
| `GoogleSignInService` / `GoogleCalendarService` | ⚠ `Endpoints/SyncCalendarToGoogleTests.cs` (3 facts) — partial | `TEST-19` ⚠ partial |
| `UserDefaultsService` | ⚠ covered indirectly via `RegistrationTests` | — |
| `RoutineTimePeriod` reset-anchor-day check constraint | ✅ new — `RoutineTimePeriodRangeConstraintTests.cs` (10 facts) pins a real bug fix in this changeset: the old constraint's `OR`/`AND` precedence let `reset_anchor_day` skip validation on the `length_in_days <= 7` branch, and the valid range was off by one (`1..7`/`1..30` → `0..7`/`0..30`). Fixed via migration `20260817130655_FixRoutineTimePeriodResetAnchorDayCheckConstraint` | — |

### Honest coverage number

**~55% "you can ship this" coverage**, weighted by risk rather than line count — up from ~25% in the
prior pass.

Reasoning: auth, registration, refresh tokens, module wiring, the pure routine math, and — as of this
batch — Activity/Activity*Profile grids, Planning, TodoLists, both routine jobs, the view-refresh
interceptor, the completion fan-out, and the default-user seeder are all now covered with auth
matrices and/or failure-injection tests. Still untested or happy-path-only: History dashboards,
Tracking ledgers, the Serilog/PII pipeline (`SEC-1`), export/account deletion, Timer CRUD, the 12
per-user default seeders, and most of the row_version concurrency surface outside the one new
completion-fan-out case.

**The riskiest gaps, in order (revised):**

1. **The untouched endpoint areas** (`TEST-1`, `TEST-6`, `TEST-7`, `TEST-9`, `TEST-10`) — Timer
   presets, History dashboards, Tracking ledgers, export/deletion, and Timer CRUD still have zero or
   auth-only coverage, and per the top-of-file scope warning these sit partly in the unreviewed
   42/712-file surface too.
2. **Logging/PII pipeline (`SEC-1`) and tracking ledgers (`SEC-4`/`SEC-5`)** remain untested — these
   were and still are the highest-severity components with zero test files.
3. **Concurrency (`row_version`)** — one test exists now (in `CompletionFanOutTests.cs`); the
   `CQ-9`/`CQ-11`/`TodoListItemIsDoneChanged` unhandled-`DbUpdateConcurrencyException` concern is
   otherwise still unverified across the rest of the entity surface.

## 12. Missing tests — backlog

Prompt files were written under `review/portal/testingPrompts/`. **As of 2026-08-17 all eight
prompted items below have been implemented** (24 new test files, ~286 new `[Fact]`/`[Theory]`
methods) — see §8 for the file-by-file mapping. Kept here for traceability; new backlog items follow.

| ID | Gap | Prompt file | Status |
|---|---|---|---|
| `TEST-3` | Planner-task / calendar / template endpoints — CRUD, auth matrix, IDOR, template overlap resolution | `PlannerTaskEndpoints.md` | ✅ resolved |
| `TEST-4` | To-do list + routine list endpoints — toggle fan-out, steps, display order, IDOR | `TodoListEndpoints.md` | ✅ resolved |
| `TEST-5` | Activity + `Activity*Profile` grids — the unfiltered hand-scoped path | `ActivityProfileGrids.md` | ✅ resolved |
| `TEST-12` | `RoutineTodoListResetJob` — grace persistence (`CQ-2`), step reset (`CQ-3`), double-fire idempotency | `RoutineTodoListResetJob.md` | ✅ resolved |
| `TEST-13` | `RoutinePeriodNudgeJob` — mid-loop notifier failure must not lose markers (`CQ-5`) | `RoutinePeriodNudgeJob.md` | ✅ resolved |
| `TEST-14` | `SuggestionPatternRefreshInterceptor` — post-commit throw (`CQ-9`), stale flags (`CQ-10`) | `SuggestionPatternRefreshInterceptor.md` | ✅ resolved |
| `TEST-16` | Completion fan-out event handlers — incl. the two dead events (`CQ-8`) | `CompletionFanOutEventHandlers.md` | ✅ resolved — but the new tests indicate `CQ-6`/`CQ-7`/`CQ-8` no longer reproduce on current code; re-verify those IDs in `02-findings.md` |
| `TEST-17` | `DefaultUsersSeeder` — failed `CreateAsync` must not proceed (`CQ-1`) | `DefaultUsersSeeder.md` | ✅ resolved |

Not written (blocked on the unreviewed surface, still open): `TEST-1` (Timer preset auth), `TEST-2`
(Reminder auth, partially pre-existing), `TEST-6` (History dashboards), `TEST-7` (Tracking),
`TEST-8` (Google Calendar — now ⚠ partially covered by `SyncCalendarToGoogleTests.cs`, not from a
written prompt), `TEST-9` (export/deletion), `TEST-10` (Timer CRUD), `TEST-15`
(`SuggestionPatternViewInstaller` vs the test fixture's parallel implementation — partially touched),
`TEST-18` (12 per-user default seeders), `TEST-19` (Google services — ⚠ partially covered). Write
prompts for these after the endpoint fan-out completes on the remaining unreviewed surface.

**New items surfaced by this batch, not yet in `02-findings.md`:**

- `PlannerDeleteBehaviorTests.cs` found Calendar delete is `Restrict` (not `Cascade`) and Activity
  delete `Cascade`s to PlannerTask — both contradict `domain-map.md`'s documented delete behavior.
  Worth a doc fix or a findings entry.
- The `RoutineTimePeriod` reset-anchor-day check constraint had a real `OR`/`AND` precedence bug
  (validation skippable on one branch) and an off-by-one range, fixed in this changeset via
  migration `20260817130655_FixRoutineTimePeriodResetAnchorDayCheckConstraint` and pinned by
  `RoutineTimePeriodRangeConstraintTests.cs`.
