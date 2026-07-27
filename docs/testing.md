# Testing — infrastructure & conventions

Reference for writing integration & unit tests in this repo. Portal-agnostic guidance; concrete examples use the HBCleaning portal (`MojaDigitalnaFirma.HBCleaning.Tests`).

## Stack

xunit v3 + FluentAssertions 7.2.2 + `Microsoft.AspNetCore.Mvc.Testing` + `Testcontainers.PostgreSql` + Moq + Respawn. Target framework: net10.0.

Each portal under test has its own `*.Tests` project (e.g. `MojaDigitalnaFirma.HBCleaning.Tests`). Tests run the **real** portal `Program` (`AdminPortal`-derived) against a Postgres container, with only auth and a couple of singletons swapped.

## Test container fixture

`infrastructure/PostgresContainerFixture.cs` (`[CollectionDefinition("Postgres")]`):

- Spins one Postgres container per test run (`Testcontainers.PostgreSql`), creates the schema via `EnsureCreatedAsync`, and runs any portal-specific SQL that EF won't (e.g. materialized views — HBCleaning creates `materialized_view_attendance_by_days` here).
- Exposes `ConnectionString`, `CreateDbContext()`, and `ResetAsync()` for inter-test cleanup (Respawn).
- Holds **cached `HbTestWebApplicationFactory` instances** for the common role combinations: `AdminAndUserFactory`, `AdminFactory`, `UserFactory`, `RootFactory`, `UnauthenticatedFactory`. For one-off custom-user / custom-role tests, call `fixture.CreateFactory(roles, userId)` — caller disposes.

## Auth: one handler, one factory, role-parametrized

There is exactly one auth handler and one factory class. They're parametrized at construction time:

- **`RoleTestAuthHandler`** (`AuthenticationHandler<RoleTestAuthHandlerOptions>`) — turns the request into a `ClaimsPrincipal` with whatever roles + user id you pass in `Options`. Scheme name: `"Test"`.
- **`HbTestWebApplicationFactory`** — builds the test host. Pass `roles: string[]?` and optional `userId`.
  - `roles = ["Admin","User"]` (or `AdminAndUserRoles` / `AdminRoles` / `UserRoles` / `RootRoles`) → registers `RoleTestAuthHandler` with that role set.
  - `roles = null` → registers no auth handler at all; the real auth pipeline challenges anonymous requests with 401.

Don't add new role-specific handler or factory classes — pass roles in instead.

## Base test class

`infrastructure/PostgresTestBase.cs` is the root of every integration test. It:

- Calls `fixture.ResetAsync()` then `SeedAsync(db)` on each test via xunit's `IAsyncLifetime`.
- Exposes typed client helpers:
  - `CreateClient()` — Admin + User (default happy-path client).
  - `CreateAdminRoleClient()` — Admin only.
  - `CreateUserRoleClient()` — User only.
  - `CreateRootRoleClient()` — Root only.
  - `CreateUnauthenticatedClient()` — no auth (used for 401 tests).
  - `CreateFactory(roles, userId)` — for cross-user / IDOR tests that need a different test user id. Caller disposes the returned factory.
- Exposes `CreateDbContext()`, `Fixture`, `ConnectionString`, and `JsonOpts` (web defaults + `JsonStringEnumConverter`).

Domain-specific seed bases extend `PostgresTestBase` and override `SeedAsync` — e.g. `AttendanceTestBase` (HBCleaning) seeds a default `Employee Emp` linked to `FakeLoggedUserService.TestUserId`.

## Standard test-class shape

```csharp
[Collection("Postgres")]
public class MyEndpointTests(PostgresContainerFixture fixture) : PostgresTestBase(fixture)
{
    [Fact]
    public async Task HappyPath_Returns200()
    {
        await using var db = CreateDbContext();
        // seed FKs here...

        var response = await CreateClient().GetAsync("/api/thing/1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

Inherit from a more specific base if one already covers your shape (see below).

## Base test classes for standard endpoint patterns

All under `infrastructure/baseTests/`. Each is abstract — concrete subclasses provide `EndpointUrl` + seeding + (where relevant) a payload builder. Each base ships happy-path + auth-matrix (`UserRole_Forbidden`, `Unauthenticated`) + `NotFound` where applicable. **Add endpoint-specific scenarios (validation, business rules, IDOR) in the concrete class.**

The write bases (`BaseUpdate`/`BasePatch`/`BaseDelete`/`BaseBatchDelete`/`BaseToggleIsHidden` test classes) additionally ship an **opt-in `NotOwner_Returns403`** test that exercises the `AuthorizeAsync(entity)` ownership hook on the matching FastEndpoints base. It is skipped (early-returns as a no-op) unless the concrete class overrides `SeedEntityOwnedByOtherUserAsync(db)` to seed an entity the default `CreateClient()` user must not be allowed to mutate (return its id). Override it for any user-scoped endpoint whose FastEndpoints base overrides `AuthorizeAsync`.

Mirrors the FastEndpoints base classes documented in the root `CLAUDE.md` (`endpoint/base/`):

| Base test class                       | Matches FastEndpoints base                      | Concrete must provide |
|---------------------------------------|-------------------------------------------------|------------------------|
| `BaseGetByIdEndpointTests`            | `BaseGetByIdEndpoint`                           | `EndpointUrl`, `SeedEntityAsync(db) -> id` |
| `BaseGetAllEndpointTests`             | `BaseGetAllEndpoint`                            | `EndpointUrl`, `SeedEntityAsync(db) -> id` |
| `BaseGetSelectOptionsEndpointTests`   | `BaseGetSelectOptionsEndpoint`                  | `EndpointUrl`, `SeedEntityAsync(db) -> id` |
| `BaseFilterEndpointTests`             | `BaseFilterEndpoint`                            | `EndpointUrl`, `SeedEntityAsync`, optionally override `EmptyFilterPayload()` |
| `BaseSortEndpointTests`               | `BaseSortEndpoint`                              | `EndpointUrl`, `SeedEntityAsync` |
| `BaseFilterSortEndpointTests`         | `BaseFilterSortEndpoint`                        | `EndpointUrl`, `SeedEntityAsync` |
| `BaseGridEndpointTests`               | `BaseGridEndpoint`                              | `EndpointUrl`, `SeedEntityAsync`. Provides `GridBody(useFilter, filter)` + `GridResponse` record. |
| `BaseCreateEndpointTests`             | `BaseCreateEndpoint`                            | `EndpointUrl`, `BuildValidPayloadAsync(db)` |
| `BaseUpdateEndpointTests`             | `BaseUpdateEndpoint`                            | `EndpointUrl`, `SeedEntityAsync`, `BuildValidPayloadAsync(db, id)` |
| `BasePatchEndpointTests`              | `BasePatchEndpoint`                             | `EndpointUrl`, `SeedEntityAsync`, `BuildValidPayloadAsync(db, id)` |
| `BaseDeleteEndpointTests`             | `BaseDeleteEndpoint`                            | `EndpointUrl`, `SeedEntityAsync` |
| `BaseBatchDeleteEndpointTests`        | `BaseBatchDeleteEndpoint`                       | `EndpointUrl`, `SeedEntitiesAsync(db, count) -> ids` |
| `BaseToggleIsHiddenEndpointTests`     | `BaseToggleIsHiddenEndpoint`                    | `EndpointUrl`, `SeedEntityAsync` |

If your endpoint is attendance-scoped in HBCleaning, prefer `AttendanceBaseGetByIdEndpointTests` / `AttendanceBaseGridEndpointTests` — they pre-seed `Emp` linked to the test user.

If your endpoint doesn't fit any of these shapes, inherit from `PostgresTestBase` directly and write the auth matrix yourself (happy path, `UserRole → 403`, `Unauthenticated → 401`).

## Seed helpers

Per-domain static helper classes named `<Domain>SeedHelper` (e.g. `AttendanceSeedHelper`, `ComplaintSeedHelper`, `ApartmentBuildingSeedHelper`, `InventorySeedHelper`). Each exposes `Seed<Thing>(db, …)` returning the inserted entity. Prefer these over hand-rolled inserts so FK chains stay consistent.

## Conventions

- Test class: `[Collection("Postgres")]`, constructor takes `PostgresContainerFixture fixture` and extends `PostgresTestBase(fixture)` (or a domain base).
- HTTP tests use `CreateClient()` / `CreateUserRoleClient()` / `CreateUnauthenticatedClient()` etc. — do not `new` up factories directly.
- Pure DB / service unit tests can use `fixture.CreateDbContext()` and `Moq` for collaborators.
- After mutating an entity over HTTP, call `db.ChangeTracker.Clear()` before re-reading via the same DbContext — EF otherwise returns the tracked stale snapshot.
- Pin "id not found" assertions to a clearly out-of-range value (`99999999` or `long.MaxValue`) so they don't collide with auto-generated ids.
- For unfixed behavior, mark the test with `[Trait("Status", "KnownGap")]` and a comment explaining the deferred fix. Do **not** soften assertions to make them pass.
- When a test encodes a legal rule (HBCleaning attendance, etc.), cite the paragraph in the test name and the `because:` string. Existing tests use Slovak labor-law conventions: `§<paragraph> ZP` or `zákon NNN/YYYY §<paragraph>`.
- Be deliberate about which client you use. `CreateClient()` is Admin+User and bypasses most checks — wrong choice for tests that exercise role gates or ownership rules.

## Custom user id / cross-user tests

For ownership / IDOR tests where the logged-in user must differ from the seeded entity owner:

```csharp
await using var f = CreateFactory(HbTestWebApplicationFactory.AdminRoles, userId: 99888777L);
using var client = f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
var response = await client.GetAsync($"/api/leave/{otherUsersLeaveId}");
```
