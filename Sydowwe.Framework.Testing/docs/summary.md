# Sydowwe.Framework.Testing — Agent Summary

**Purpose:** Portal-agnostic test infrastructure — one Postgres-container fixture, one test base, one role-parametrized auth handler + host factory, and one abstract test class per Framework base
endpoint. Portals supply only a closed subclass.

**Bounded context:** Owns the *generic* test scaffolding (container lifecycle, Respawn reset, auth swapping, the auth-matrix test bases). It owns **no** portal schema, seed data, or feature tests —
those live in the portal's own test project via the override hooks.

## The pieces

- **`PostgresContainerFixture<TProgram, TDbContext>`** (`IPostgresFixture`, collection fixture) — boots a Postgres 17 Testcontainer (host port 5439),
  `EnsureCreated`, then caches one `TestWebApplicationFactory` per role combo (Admin+User, Admin, User, Root, unauthenticated). Hooks a portal overrides:
  `NewDbContext` (required), `OnSchemaCreatedAsync` (materialized views/objects EF skips), `SeedFixtureAsync` (baseline after create **and after every reset**),
  `AfterResetAsync` (e.g. `REFRESH MATERIALIZED VIEW`). Enables legacy timestamp behavior in a static ctor.
- **`PostgresTestBase`** — base for test classes. Depends only on
  `IPostgresFixture`. Per-test `InitializeAsync` runs `ResetAsync()` (Respawn) then
  `SeedAsync(db)` (override per class). Client factories: `CreateClient()`
  (Admin+User happy path), `CreateAdminRoleClient` / `CreateUserRoleClient` /
  `CreateRootRoleClient` (single-role, no auto-redirect, for role gates),
  `CreateUnauthenticatedClient` (real auth → 401), and
  `CreateFactory(roles, userId, configureServices)` for cross-user IDOR / per-test service overrides (caller disposes). `CreateDbContext()` for direct DB asserts/seeding. `JsonOpts` = web defaults +
  enum-as-string.
- **`TestWebApplicationFactory<TProgram>`** (`ITestClientFactory`) — spins the real portal host; swaps the `NpgsqlDataSource` to the container, registers a
  `FakeLoggedUserService`, and (when `roles != null`) the `RoleTestAuthHandler`
  scheme. `roles == null` ⇒ leave the real auth layer in place (anonymous → 401). Sets all required env vars. `_configureServices` runs **last** so per-test fakes win over host registrations.
- **`RoleTestAuthHandler`** ("Test" scheme) — issues a ClaimsPrincipal with
  `NameIdentifier` = userId, email, and a role claim per requested role.
- **`FakeLoggedUserService`** / **`TestRoles`** — the canned test user id/email and the role-array constants (`AdminAndUser`, `Admin`, `User`, `Root`).
- **`baseTests/`** — one abstract test class per Framework base endpoint (`BaseGridEndpointTests`, `BaseGetByIdEndpointTests`, `BaseCreateEndpointTests`,
  `BaseUpdateEndpointTests`, `BasePatchEndpointTests`, `BaseDeleteEndpointTests`,
  `BaseBatchDeleteEndpointTests`, `BaseGetAllEndpointTests`,
  `BaseGetSelectOptionsEndpointTests`, `BaseFilterEndpointTests`,
  `BaseSortEndpointTests`, `BaseFilterSortEndpointTests`,
  `BaseToggleIsHiddenEndpointTests`). Each ships the **auth matrix** (403 for wrong role, 401 unauthenticated) + 404/happy-path; you supply `EndpointUrl` and a `SeedEntityAsync`. The handler and
  factory are **role-parametrized — do not add per-role subclasses.**

## Gotchas

- One container per collection; `ResetAsync` (Respawn) runs **before each test**, then re-seeds the fixture baseline + `AfterResetAsync`. Don't assume rows from a previous test survive.
- Single-role and unauthenticated clients use `AllowAutoRedirect = false` so tests observe the raw 401/403 instead of following the auth challenge redirect.
- `SeedFixtureAsync` runs on **every** reset — keep it idempotent and cheap.
- Materialized views aren't created by `EnsureCreated`; create them in
  `OnSchemaCreatedAsync` and refresh them in `AfterResetAsync`.

## Extension playbook

- **Add the test foundation to a new portal:** subclass
  `PostgresContainerFixture<Program, XDbContext>`, implement `NewDbContext`, add a
  `[CollectionDefinition("Postgres")]`; point test classes at `PostgresTestBase`.
- **Test a new endpoint built on a Framework base:** subclass the matching
  `baseTests/*` class, set `EndpointUrl`, implement `SeedEntityAsync`; add endpoint-specific scenarios (validation, business rules, IDOR) in the subclass.
- **Test as a different user (IDOR/ownership):** `CreateFactory(roles, userId)`.
- **Swap a host service for a fake:** pass `configureServices` to `CreateFactory`.

## See also

- Solution-wide guide: `../docs/testing.md`
- Endpoints under test: `../Sydowwe.Framework/architecture.md`
