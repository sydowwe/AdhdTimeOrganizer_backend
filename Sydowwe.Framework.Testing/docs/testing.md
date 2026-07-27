# Sydowwe.Framework.Testing — Testing

How to test against this foundation, and the strategy the base classes encode. See `../docs/testing.md` for the solution-wide guide; this file is the framework-test-infra-specific layer.

## How to test this module

The library itself has no `[Fact]`s to run — it ships abstract bases. To use it, a portal test project provides:

- **Fixture:** `class XFixture : PostgresContainerFixture<Program, XDbContext>`
  overriding `NewDbContext` (required) and any of `OnSchemaCreatedAsync` /
  `SeedFixtureAsync` / `AfterResetAsync`.
- **Collection:** `[CollectionDefinition("Postgres")] class …Collection :
  ICollectionFixture<XFixture>` — one container shared across the collection.
- **Test classes:** subclass `PostgresTestBase` (general) or a `baseTests/*` class (one per Framework base endpoint), under `[Collection("Postgres")]`.

Requirements: Docker (Testcontainers boots Postgres 17 on host port 5439). The fixture sets all env vars the portal `Program` expects, so no `appsettings`
wiring is needed in tests.

### Fixtures & helpers

| Piece                                                        | Role                                                                           |
|--------------------------------------------------------------|--------------------------------------------------------------------------------|
| `PostgresContainerFixture<TProgram,TDbContext>`              | container lifecycle, `EnsureCreated`, cached per-role factories, Respawn reset |
| `IPostgresFixture`                                           | the portal-agnostic surface `PostgresTestBase` depends on                      |
| `PostgresTestBase`                                           | per-test reset+seed; client factory methods; `CreateDbContext()`; `JsonOpts`   |
| `TestWebApplicationFactory<TProgram>` (`ITestClientFactory`) | real host with swapped data source + auth; `configureServices` runs last       |
| `RoleTestAuthHandler` (`"Test"` scheme)                      | issues a principal with userId + email + role claims                           |
| `FakeLoggedUserService` / `TestRoles`                        | canned test user; role-array constants                                         |
| `baseTests/*`                                                | abstract endpoint tests carrying the auth matrix + 404/happy path              |

### Client cheat-sheet (`PostgresTestBase`)

| Method                                            | Identity         | Use for                                          |
|---------------------------------------------------|------------------|--------------------------------------------------|
| `CreateClient()`                                  | Admin + User     | happy path                                       |
| `CreateAdminRoleClient()`                         | Admin only       | admin-gated endpoints (no auto-redirect)         |
| `CreateUserRoleClient()`                          | User only        | assert 403 on admin endpoints                    |
| `CreateRootRoleClient()`                          | RootAdmin        | root-only paths                                  |
| `CreateUnauthenticatedClient()`                   | none (real auth) | assert 401                                       |
| `CreateFactory(roles, userId, configureServices)` | custom           | cross-user IDOR, service fakes (caller disposes) |

## Strategy

- **Integration over unit.** The bases run the **real portal `Program`** against a real Postgres, so endpoint tests exercise routing, auth, validation, EF mapping, and the audit interceptor
  end-to-end. Reserve plain unit tests for pure logic (calculators, renderers) with no DB/HTTP edge.
- **One abstract test base per Framework base endpoint.** Subclass the matching
  `baseTests/*` class to inherit the **auth matrix** (wrong-role → 403, unauthenticated → 401) and 404/happy-path for free; you only supply
  `EndpointUrl` + `SeedEntityAsync`. Put endpoint-specific cases (validation, business rules, ownership/IDOR) in the concrete subclass.
- **Role/factory parametrization, not subclassing.** The auth handler and factory take roles + userId as parameters — never add per-role handler/factory subclasses.
- **Isolation via Respawn.** Each test starts from the reset baseline (`ResetAsync` → `SeedFixtureAsync` → `AfterResetAsync` → the class's
  `SeedAsync`). Keep `SeedFixtureAsync` idempotent — it runs on every reset.

## Bespoke patterns

- **Materialized views.** `EnsureCreated` does not create them — define them in
  `OnSchemaCreatedAsync` and `REFRESH` them in `AfterResetAsync` (Respawn truncates the underlying tables each test).
- **Legacy timestamps.** The fixture's static ctor enables
  `Npgsql.EnableLegacyTimestampBehavior` so `DateTime` columns behave as tests expect.
- **Per-test service swaps.** Pass `configureServices` to `CreateFactory` to fake a host service (e.g. an external document/SharePoint service) — it is applied after host registrations so it wins.

## Framework coverage map

The base-endpoint HTTP/auth surface is covered by the `baseTests/*` classes (every portal endpoint built on a base inherits it). The Framework's own security/data-integrity machinery is covered
**directly** by integration tests in the portal test project (`MojaDigitalnaFirma.AdminPortal.Tests/integration/`):

| Framework behavior                                                                                                                                                                    | Tested by                                                 |
|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-----------------------------------------------------------|
| Base-endpoint auth matrix (401/403) + 404 + happy path                                                                                                                                | `baseTests/*` (one per base)                              |
| GetById wrong-role 403 + cross-user IDOR (opt-in)                                                                                                                                     | `baseTests/BaseGetByIdEndpointTests`                      |
| Command-base ownership/IDOR (Update/Patch/Delete/BatchDelete/ToggleIsHidden, opt-in)                                                                                                  | the respective `baseTests/*` `NotOwner`/`CrossUser` tests |
| Grid paging contract (`ItemsCount`/`PageCount`) + edge cases (ItemsPerPage 0/huge, Page 0/negative)                                                                                   | `infrastructure/GridPaginationEdgeTests`                  |
| Audit interceptor: insert/update/delete snapshots, `ChangedProperties`, `[AuditIgnore]`, `[NoAudit]`, timestamp-only skip, own-transaction rollback, null UserId when unauthenticated | `infrastructure/AuditInterceptorTests`                    |
| Refresh-token rotation, reuse-detection→revoke-all, expiry, hash-at-rest, concurrent-rotation race, cleanup; `RetryDbConcurrencyHelper` retry/exhaust/change-tracker reset            | `infrastructure/RefreshTokenAndRetryTests`                |
| Auth flow: login success/lockout/generic-error/recaptcha/2FA branch, refresh rotate/stale, logout, change-password + token revocation                                                 | `endpoint/auth/AuthFlowEndpointTests`                     |
| Partitioned `audit_log`: RANGE partitioning, year + default partitions exist, row routing (run against real migrations, not `EnsureCreated`)                                          | `infrastructure/AuditPartitioningMigrationTests`          |

## Known gaps (living list)

- **IDOR coverage is opt-in per endpoint.** The cross-user tests in
  `BaseGetByIdEndpointTests` and the command bases default to `Assert.Skip` unless the concrete subclass overrides `SeedEntityOwnedByOtherUserAsync`. A
  `IEntityWithUser` endpoint that omits the seed gets **no** ownership coverage — the base can't enforce it. When the endpoint under test sets
  `BaseGetByIdEndpoint.NotFoundWhenUnauthorized` (existence is confidential → a failed authorize answers 404, not 403), also override
  `BaseGetByIdEndpointTests.UnauthorizedStatus => HttpStatusCode.NotFound` — that pairing is what pins the flag. Seed the caller a real owning record too, otherwise the refusal you assert may just be
  "this user resolves to nobody" rather than "this user is not the owner".
- **`FakeLoggedUserService` fidelity gap.** It always reports
  `IsAuthenticated == true` and roles `[Admin]`, so `ClaimsPrincipal` vs
  `ILoggedUserService` can never diverge in a test — a production bug where the two
  "current user" sources disagree (e.g. UserId stamping on a background/system save)
  is invisible. The audit interceptor's null-UserId path is covered via the unauthenticated factory, not via this fake.
- No self-tests for the library's own abstractions (`RoleTestAuthHandler`,
  `PostgresContainerFixture` lifecycle) — exercised only transitively.
- `baseTests/` covers the **base** endpoint behavior; portal-specific endpoints built without a Framework base need hand-written tests (no auth-matrix freebie).
- Single shared container per collection — heavily parallel suites serialize on it; no per-test-class DB isolation beyond Respawn.
