# AdhdTimeOrganizer (Portal) — Testing

Full guide: [`framework/Sydowwe.Framework.Testing/docs/testing.md`](../../framework/Sydowwe.Framework.Testing/docs/testing.md).
**Not** the root `docs/testing.md` — that one is a foreign copy. This page covers only what is
portal-specific.

## How to test this portal

Tests live in `AdhdTimeOrganizer.IntegrationTests` (xunit v3 + FluentAssertions + Moq + Respawn) and
run the **real** `Program` against a `Testcontainers.PostgreSql` container. Tag every DB-touching
class `[Collection("Postgres")]`.

`Infrastructure/AppDbContextFixture.cs` closes the shared `PostgresContainerFixture<Program, AppDbContext>`
and is where the portal-specific setup lives:

- **Materialized views.** `OnSchemaCreatedAsync` executes every `*.sql` from the `sqlScripts` folder
  copied next to the test binaries (a `Content` item in the test csproj). `EnsureCreated` cannot
  produce them, and without them `SuggestionPatternRefreshInterceptor` fails with 42P01 on any save
  touching `PlannerTask` / `ActivityHistory` / `Calendar` — including registration, which seeds a
  `Calendar`. **Add a new script to `infrastructure/persistence/sqlScripts/` and both this and the
  runtime installer pick it up**; nothing else needs changing.
- **Env vars** the base fixture doesn't know about (`EXTENSION_ID`, the `MAIL_*` set, the Google OAuth
  pair, `ROOT_ADMIN_EMAIL`) and an ECDSA key written to `secrets/ec_private.pem`, which
  `EcdsaKeyProvider` reads unconditionally once a request hits the real JwtBearer scheme.
- **Seeded fixture user** with a real password hash (`AppDbContextFixture.TestUserPassword`) **and**
  a `User` role row plus assignment. The role row matters: the global endpoint configurator gives
  every endpoint `Roles("User","Admin","Root")`, so a real JWT without a role claim authenticates and
  then 403s on everything. Tests using `RoleTestAuthHandler` fabricate roles in the principal and
  never read those rows.
- **Global stubs:** `IGoogleRecaptchaService` and `IUserEmailSenderService<User>` are replaced with
  Moq objects. Nothing exercises real SMTP or reCAPTCHA.
- `NewDbContext` adapts the framework's fake logged-user service down to the portal's own
  `ILoggedUserService` — that adapter is used only for contexts the fixture builds directly
  (seeding/asserting outside HTTP); requests through the host use the real one.

Clients: `CreateClient()` (Admin+User), `CreateUserRoleClient()`, `CreateAdminRoleClient()`,
`CreateRootRoleClient()`, `CreateUnauthenticatedClient()`, and `CreateFactory(roles, userId)` when a
test needs a *different* user (caller disposes). `CreateDbContext()` for direct seed/assert;
override `SeedAsync(db)` per test class.

## Strategy

- **Endpoints built on a Framework base get the matching abstract test base** from
  `Sydowwe.Framework.Testing/baseTests/` — they ship the auth matrix and the 404 paths, so the
  concrete subclass only adds validation, business-rule and IDOR cases. `Endpoints/Base*Tests.cs`
  each close one such base over a representative entity (`ActivityCategory` for the CRUD shapes,
  `Calendar` for filter, `TodoList` for filter+sort, `TemplatePlannerTask` for patch,
  `RoutineTimePeriod` for toggle-is-hidden). The Reminders endpoint tests
  (`Reminders/ReminderEndpointTests.cs`) do the same for the portal reminder CRUD.
- **Pure domain rules are unit-tested with no container** — `Services/RoutineResetServiceTests.cs`
  (reset instants, streaks, grace, nudge windows) and `Services/PerUserDefaultMatcherTests.cs`
  (per-user default collision matching). Prefer this shape for anything that is a function of its
  inputs; `RoutineResetService` is static precisely so it can be tested this way.
- **Integration-test what composition or SQL can break**, since none of it fails the build:
  `Modules/ModuleWiringTests.cs` pins the module wiring (the non-overlapping marker scans, the
  `DbContext` alias, `IQuietHoursReader` resolution, the registrars);
  `Reminders/ReminderRegistrationTests.cs` pins the registry translation;
  `Routines/RoutineNotificationTests.cs` pins the nudge/summary/grace behaviour;
  `Auth/*` drives the real Identity + JWT pipeline (login, logout, refresh, 2FA, registration,
  security cases); `Endpoints/ExtensionActivityTrackingTests.cs` covers the extension-client gate.
- **Run `ModuleWiringTests` after touching anything in `config/dependencyInjection/` or `Program.cs`.**
  Double registration, a missing FastEndpoints assembly and a missing registrar all produce a running
  app that silently misbehaves.

## Known gaps (living list)

No test carries `[Trait("Status","KnownGap")]` today; the gaps below are by inspection of what has no
coverage, not a coverage matrix.

- `ApplyTemplatePlannerTaskEndpoint` — the four conflict-resolution modes and the carve/split logic
  have no tests, and neither does the orphaned-reminder cancellation it performs.
- `GetSuggestionsRepeatingPlannerTaskEndpoint` — the three-tier de-duplication is untested, as are the
  pattern views themselves (the ≥3-occurrence threshold and the average-time computation).
- `SuggestionPatternRefreshInterceptor` — nothing asserts that a save actually refreshes, only that
  saves don't blow up.
- The tracking ingest endpoints (desktop / web-extension / Android heartbeats) have no idempotency
  test against their unique keys, and the tracking dashboards are uncovered.
- The `IsDone` event fan-out (planner task ↔ to-do item ↔ routine item) has no test.
- Google sign-in and Google Calendar sync are stubbed out entirely, not tested.
- `GetUserDataExportEndpoint` — the 1/min throttle and the export shape are uncovered.
- `RoutineTodoListResetJob` / `RoutinePeriodNudgeJob` are not driven end to end; only the
  `RoutineResetService` rules underneath them and the notification layer above them are.
