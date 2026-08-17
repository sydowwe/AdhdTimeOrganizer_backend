# TEST-1 — Timer preset auth matrix

## Context

`AdhdTimeOrganizer.IntegrationTests` is an xunit v3 project. The real portal `Program` runs against a
`Testcontainers.PostgreSql` container via `Sydowwe.Framework.Testing`'s
`PostgresContainerFixture<TProgram, TDbContext>`, closed by `Infrastructure/AppDbContextFixture.cs`.
`RoleTestAuthHandler` (scheme `"Test"`) swaps auth; `TestWebApplicationFactory<TProgram>` builds
role-parametrized clients via `CreateUserRoleClient()` / `CreateFactory(TestRoles.X)`. Tests live under
`[Collection("Postgres")]` and inherit `PostgresTestBase(fixture)`.

`Sydowwe.Framework.Testing.baseTests` ships 13 abstract CRUD/auth test bases (`BaseCreateEndpointTests`,
`BaseUpdateEndpointTests`, `BaseDeleteEndpointTests`, `BaseGetByIdEndpointTests`,
`BaseGetAllEndpointTests`, `BaseGetSelectOptionsEndpointTests`, `BaseBatchDeleteEndpointTests`,
`BasePatchEndpointTests`, `BaseFilterEndpointTests`, `BaseFilterSortEndpointTests`,
`BaseFetchTableEndpointTests`, `BaseToggleIsHiddenEndpointTests`). Subclassing one wires the full
cross-user-404 / anonymous-401 / wrong-role-403 matrix for free. `ActivityEndpointTests.cs` (see
`AdhdTimeOrganizer.IntegrationTests/Endpoints/`) is the reference example — read it before writing new
tests; copy its `Seed` helper pattern (a second user created via `UserManager<User>` inside
`fixture.UnauthenticatedFactory.Services`) rather than reinventing one.

## What exists today

`Endpoints/TimerPresetValidationTests.cs` covers happy-path and edge-case validation for
`TimerPreset` / `PomodoroTimerPreset` (both live in `AdhdTimeOrganizer.Core`,
`application/endpoint/timer/timerPreset/**` and `application/endpoint/timer/pomodoroTimerPreset/**` —
Create/Update/Delete/GetAll/GetById for each). **No auth coverage exists**: no test proves a
cross-user id 404s, that anonymous is rejected, or that the role gate matches `AllowedRoles()`.

## What to write

Add `Endpoints/TimerPresetCrudAuthMatrixTests.cs`. For both `TimerPreset` and `PomodoroTimerPreset`,
subclass the relevant framework bases (Create/Update/Delete/GetById/GetAll at minimum — check which of
Filter/FetchTable/GetSelectOptions those endpoints actually expose before adding those bases too).
Follow `ActivityEndpointTests.cs`'s `Seed.SecondUserAsync` pattern for the cross-user id. Confirm
whether these entities are `IEntityWithUser` (global query filter → expect 404 on cross-user id) or
hand-scoped (expect whatever `ApplyCustomFiltering` does) before picking `UnauthorizedStatus` overrides
— don't assume 404 without checking, per `ActivityEndpointTests.cs`'s own comment about this trap.

## Out of scope

Don't touch validation edge cases — `TimerPresetValidationTests.cs` already owns those and this task
is additive, not a rewrite.
