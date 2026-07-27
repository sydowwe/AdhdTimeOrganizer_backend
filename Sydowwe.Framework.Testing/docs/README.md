# Sydowwe.Framework.Testing

> The reusable test foundation: a Postgres-container fixture, a portal-agnostic
> test base, a role-parametrized auth handler + host factory, and one abstract
> test class per Framework base endpoint.

## What it does

This library ships the **shared, portal-agnostic** test infrastructure so each portal's test project only supplies a closed subclass and its concrete
`DbContext`. Tests run the **real portal `Program`** against a
`Testcontainers.PostgreSql` container (Postgres 17), with auth and a couple of singletons swapped out. Stack: xUnit v3 + FluentAssertions + Respawn +
`Microsoft.AspNetCore.Mvc.Testing`.

It is `IsPackable` — it carries abstract base classes, not runnable `[Fact]`s of its own.

## Setup / running

A portal test project wires it up by supplying:

1. A closed fixture: `class XFixture : PostgresContainerFixture<Program, XDbContext>`
   overriding `NewDbContext` (+ `OnSchemaCreatedAsync` / `SeedFixtureAsync` /
   `AfterResetAsync` as needed).
2. `[CollectionDefinition("Postgres")] class … : ICollectionFixture<XFixture>`.
3. Test classes subclassing `PostgresTestBase` (or one of the `baseTests/`
   abstract endpoint test bases) under `[Collection("Postgres")]`.

Docker must be available for the Testcontainers Postgres instance.

## Docs

- `summary.md` — start here; the fixture/base/auth model and how to extend it.
- Solution-wide guide: `../docs/testing.md`.
- The endpoints these mirror: `../Sydowwe.Framework/architecture.md`.
