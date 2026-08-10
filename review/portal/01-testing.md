# Portal review — 01 · Testing

> ⚠ **Scope warning.** This review covers **42 of 712** portal source files (~6%). The fan-out was
> halted by an API session limit before reaching `application/endpoint/**` (275 files),
> `application/validator/**` (62), `application/dto/**` (198), and most of
> `infrastructure/persistence/configuration/**` and `seeder/**`. The coverage matrix below is
> therefore **not** a portal-wide endpoint matrix — it is a matrix over the reviewed surface plus
> what the test project itself reveals. See `00-STATUS.md` and `REMAINING.txt`.

## Test infrastructure summary

The portal has one integration-test project, `AdhdTimeOrganizer.IntegrationTests`, holding **104
`[Fact]`/`[Theory]` methods across 10 concrete test classes**. It follows the documented framework
pattern exactly: the real portal `Program` runs against a `Testcontainers.PostgreSql` container via
`Sydowwe.Framework.Testing`'s `PostgresContainerFixture<TProgram, TDbContext>`, closed here by
`Infrastructure/AppDbContextFixture.cs`, with `RoleTestAuthHandler` (scheme `"Test"`) swapping auth
and `TestWebApplicationFactory<TProgram>` building role-parametrized clients. Tests are tagged
`[Collection("Postgres")]`; xunit v3 + FluentAssertions + Moq + Respawn.

The `Endpoints/Base*EndpointTests.cs` files are the **abstract framework test bases** (13 of them,
one per FastEndpoints base), not concrete tests — they contain no `[Fact]`s of their own and
contribute nothing to the 104 until a portal endpoint subclasses one. **Nothing in the portal
currently does.** That is the single largest structural gap in the suite: the auth matrix and 404
paths those bases ship are written and unused.

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

**Weaknesses.**

- **The 13 abstract endpoint test bases are unused.** They ship the auth matrix (each role × each
  route) and the 404 path for free. Not subclassing them means the portal's ~275 endpoints have
  **no systematic authorization coverage at all** — the exact surface where CLAUDE.md warns
  `ApplyUserScoping` is a no-op and where IDOR would live.
- **Coverage is concentrated where the code is already safest.** Auth, wiring and pure domain math
  are well covered; the areas that produced the highest-severity findings — the Serilog/logging
  pipeline (`SEC-1`), the view-refresh interceptor (`PERF-1`/`CQ-9`), the tracking ledgers
  (`SEC-4`/`SEC-5`) — have **no** test files.
- **Two shipped defects prove the event-handler paths are untested.** `CQ-8` found that
  `ActivityAddedToHistoryEvent` and `ActivityCreatedIsOnTodoListEvent` are **never published
  anywhere**, so their handlers are dead code. A single integration test asserting "creating an
  activity with IsOnTodoList creates a TodoListItem" would have caught this immediately. Likewise
  `CQ-3` (routine reset never unticks `Steps`) survives because `RoutineResetServiceTests` tests the
  pure service, not the job's `Include` chain — the bug is in the **query**, which the unit test
  cannot see.
- **Assertion theater risk is concentrated in the job/notification tests.**
  `Routines/RoutineNotificationTests.cs` is the only coverage of the nudge sweep, and `CQ-5` (one
  failing notification aborts the loop and loses every idempotency marker) is precisely the kind of
  failure a happy-path-only test cannot detect. If that class does not have a test where the
  notifier *throws*, it is asserting the easy half of the contract.
- **No concurrency coverage.** `row_version` is on every table via `BaseEntityConfigure`, and three
  separate fragments (`CQ-9`, `CQ-11`, and the `TodoListItemIsDoneChanged` handler) flag unhandled
  `DbUpdateConcurrencyException` turning benign races into 500s. Nothing tests a concurrent write.

## 8. Coverage matrix

### Endpoints

**No portal endpoint was reviewed in this pass**, and no portal endpoint subclasses a framework test
base. Rather than fabricate a per-endpoint table I can't support, here is what is actually known,
derived from the test project and `docs/domain-map.md`'s "Notable endpoints" list:

| Endpoint area (domain-map) | Happy | Edge | Auth | Test file | ID |
|---|---|---|---|---|---|
| Auth / registration / sessions | ✅ | ✅ | ✅ | `Auth/*` (3 files) | — |
| Extension activity-tracking ingest | ✅ | ⚠ partial | ✅ | `Endpoints/ExtensionActivityTrackingTests.cs` | — |
| Timer preset validation | ✅ | ✅ | ❌ | `Endpoints/TimerPresetValidationTests.cs` | `TEST-1` |
| Reminder CRUD + day view | ✅ | ⚠ partial | ❌ | `Reminders/ReminderEndpointTests.cs` | `TEST-2` |
| **Activity planning** (calendar, planner tasks, templates, suggestions) | ❌ | ❌ | ❌ | *none* | `TEST-3` |
| **To-do lists** (lists, items, steps, priorities, routine lists) | ❌ | ❌ | ❌ | *none* | `TEST-4` |
| **Activity** (activity, roles, categories, 4 lookups, anchors, 3 profiles) | ❌ | ❌ | ❌ | *none* | `TEST-5` |
| **Activity history + dashboards** | ❌ | ❌ | ❌ | *none* | `TEST-6` |
| **Desktop / Android tracking + dashboards** | ❌ | ❌ | ❌ | *none* | `TEST-7` |
| **Google Calendar endpoints** | ❌ | ❌ | ❌ | *none* | `TEST-8` |
| **User data export / account deletion** | ❌ | ❌ | ❌ | *none* | `TEST-9` |
| Timers (`TimerPreset`, `PomodoroTimerPreset`) CRUD | ❌ | ❌ | ❌ | *none* | `TEST-10` |

`TEST-5` and the three `Activity*Profile` grids deserve special weight: per `domain-map.md` those
entities are **not** `IEntityWithUser`, have **no** global query filter, and scope by hand in
`ApplyCustomFiltering`. That is an untested, unfiltered, hand-rolled scoping path — the highest
a-priori IDOR risk in the portal.

### Services, jobs, handlers, seeders (reviewed surface only)

| Component | Tests | ID |
|---|---|---|
| `RoutineResetService` (pure domain) | ✅ `Services/RoutineResetServiceTests.cs` | — |
| `RefreshTokenService` | ✅ `Services/RefreshTokenServiceTests.cs` | — |
| `PerUserDefaultMatcher` | ✅ `Seeding/PerUserDefaultMatcherTests.cs` | — |
| `ReminderRegistrationService` | ✅ `Reminders/ReminderRegistrationTests.cs` | — |
| Routine notifications | ⚠ `Routines/RoutineNotificationTests.cs` — failure path unproven | `TEST-11` |
| Module wiring / composition | ✅ `Modules/ModuleWiringTests.cs` | — |
| `RoutineTodoListResetJob` | ❌ — `CQ-2`, `CQ-3` both live here | `TEST-12` |
| `RoutinePeriodNudgeJob` | ❌ — `CQ-5` lives here | `TEST-13` |
| `SuggestionPatternRefreshInterceptor` | ❌ — `PERF-1`, `CQ-9`, `CQ-10` | `TEST-14` |
| `SuggestionPatternViewInstaller` | ❌ (fixture reimplements it instead) | `TEST-15` |
| 5 event handlers | ❌ — `CQ-6`, `CQ-7`, `CQ-8` | `TEST-16` |
| `DefaultUsersSeeder` | ❌ — `CQ-1` | `TEST-17` |
| 12 per-user default seeders | ⚠ matcher only, no seeder-level test | `TEST-18` |
| `GoogleSignInService` / `GoogleCalendarService` | ❌ | `TEST-19` |
| `UserDefaultsService` | ⚠ covered indirectly via `RegistrationTests` | — |

### Honest coverage number

**~25% "you can ship this" coverage**, weighted by risk rather than line count.

Reasoning: auth, registration, refresh tokens, module wiring and the pure routine math are properly
covered and I would ship those. Everything else is either untested or tested only on its happy path.
Of the eight 🟠-or-worse findings in `02-findings.md` that sit in reviewed code, **seven are in
components with zero tests** — that ratio is the real coverage signal.

**The riskiest gaps, in order:**

1. **Read/IDOR across all ~275 endpoints** (`TEST-3`–`TEST-10`). `ApplyUserScoping` is a no-op
   virtual, the framework test bases that would cover this are unsubclassed, and the
   `Activity*Profile` grids scope by hand with no filter behind them.
2. **Rollover and batch paths** (`TEST-12`, `TEST-13`). Both routine jobs carry live 🟠 defects
   (`CQ-2`, `CQ-3`, `CQ-5`) that only a job-level integration test can catch, because the bugs are
   in the query shape and the loop's failure handling, not in the pure service the unit tests cover.
3. **The completion fan-out** (`TEST-16`). `domain-map.md` documents it as a load-bearing product
   behavior; three of its handlers have defects and two are dead code.

## 12. Missing tests — backlog

Prompt files written under `review/portal/testingPrompts/`. Each is self-contained for a
context-less agent.

| ID | Gap | Prompt file |
|---|---|---|
| `TEST-3` | Planner-task / calendar / template endpoints — CRUD, auth matrix, IDOR, template overlap resolution | `PlannerTaskEndpoints.md` |
| `TEST-4` | To-do list + routine list endpoints — toggle fan-out, steps, display order, IDOR | `TodoListEndpoints.md` |
| `TEST-5` | Activity + `Activity*Profile` grids — the unfiltered hand-scoped path | `ActivityProfileGrids.md` |
| `TEST-12` | `RoutineTodoListResetJob` — grace persistence (`CQ-2`), step reset (`CQ-3`), double-fire idempotency | `RoutineTodoListResetJob.md` |
| `TEST-13` | `RoutinePeriodNudgeJob` — mid-loop notifier failure must not lose markers (`CQ-5`) | `RoutinePeriodNudgeJob.md` |
| `TEST-14` | `SuggestionPatternRefreshInterceptor` — post-commit throw (`CQ-9`), stale flags (`CQ-10`) | `SuggestionPatternRefreshInterceptor.md` |
| `TEST-16` | Completion fan-out event handlers — incl. the two dead events (`CQ-8`) | `CompletionFanOutEventHandlers.md` |
| `TEST-17` | `DefaultUsersSeeder` — failed `CreateAsync` must not proceed (`CQ-1`) | `DefaultUsersSeeder.md` |

Not written (blocked on the unreviewed surface): `TEST-1`, `TEST-2`, `TEST-6`–`TEST-11`, `TEST-15`,
`TEST-18`, `TEST-19`. Write these after the endpoint fan-out completes — the prompts would otherwise
be guesses about request/response shapes I have not read.
