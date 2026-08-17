# TEST-2 — Reminder auth matrix

## Context

Same infra as the other portal integration tests: `PostgresTestBase`, `CreateUserRoleClient()`,
`Sydowwe.Framework.Testing.baseTests`'s 13 abstract CRUD/auth bases. Reminders live in
`AdhdTimeOrganizer.Planning` (per `CLAUDE.md`, Planning owns calendar, planner tasks, day templates,
**and reminders**) but are backed by the opt-in `Sydowwe.Reminders` framework module — check
`framework/Sydowwe.Reminders/` for the base entity/endpoint shapes before assuming Planning owns the
whole surface.

`Endpoints/ActivityEndpointTests.cs` and `Endpoints/PlanningCrudAuthMatrixTests.cs` are the reference
examples for this exact pattern (subclassing framework bases + a `Seed.SecondUserAsync` second-user
helper for cross-user-id checks) — read `PlanningCrudAuthMatrixTests.cs` first since it's the same
slice.

## What exists today

`Reminders/ReminderEndpointTests.cs` and `Reminders/ReminderSeedHelper.cs` (a shared seeding helper,
already the right shape per the project's own review notes) cover happy-path and partial edge cases for
reminder CRUD + the day view. **No auth coverage**: nothing proves a cross-user reminder id 404s (or
403s), that anonymous is rejected, or that role gating matches `AllowedRoles()`.

## What to write

Add an auth-matrix test class (e.g. `Reminders/ReminderCrudAuthMatrixTests.cs`) subclassing the
framework bases for whichever reminder endpoints exist (Create/Update/Delete/GetById at minimum — check
`framework/Sydowwe.Reminders/application/endpoint/` and any Planning-side reminder endpoints for the
full list, including the day-view/dashboard endpoints already covered by `ReminderEndpointTests.cs` —
those need an auth pass too if they take a user-scoped filter). Reuse
`Reminders/ReminderSeedHelper.cs` where it fits rather than duplicating seed logic. Confirm whether
reminder entities are `IEntityWithUser` (global filter → 404) before picking `UnauthorizedStatus`.

## Out of scope

Don't re-test the day-view business logic or notification dispatch — those are owned by
`ReminderEndpointTests.cs` and `Reminders/ReminderRegistrationTests.cs` respectively. This task is auth
coverage only.
