# Sydowwe.Framework

> The shared foundation every portal and Core module builds on: base entities,
> EF Core persistence helpers, auditing, base FastEndpoints, identity/auth, and
> the DI conventions that wire it all together.

## What it does

`Sydowwe.Framework` is a reusable, **cross-solution** framework (not a feature module). It owns the building blocks that don't belong to any one business domain: the base entity hierarchy, the
abstract `BaseDbContext`, EF Core builder and CRUD helper extensions, the audit-log interceptor, the generic FastEndpoints base classes, ASP.NET Identity + JWT/2FA authentication, the seeder
framework, and the `Result` type. Portals (`*.AdminPortal`) and each consuming solution's Core modules (`*.Core.*`) reference it and specialize its abstractions.

It targets `net10.0`, EF Core 10 on Npgsql/Postgres, and FastEndpoints 8.

## Setup / running

No module-specific setup — it is a class library, not a runnable host. It is consumed by:

- a portal that derives a concrete `BaseDbContext` (the `<Family>.Core` context), wires the audit interceptor + partition SQL generator into `AddDbContext`, and calls `AddCore()` to scan-register the
  DI marker interfaces (see
  `MojaDigitalnaFirma.AdminPortal/config/CoreServiceExtensions.cs`).
- `Sydowwe.Framework.Testing`, which ships the abstract test bases that mirror these endpoints.

## Docs

- `summary.md` — **start here** if you're working in or extending the framework
- `architecture.md` — full navigation index: every base class, service, helper, and extension with its path and purpose
- The root `CLAUDE.md` also summarizes entity/audit/endpoint conventions for agents working anywhere in the solution.
- Testing foundation: `../Sydowwe.Framework.Testing/summary.md`
