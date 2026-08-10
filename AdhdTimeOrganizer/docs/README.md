# AdhdTimeOrganizer (Portal)

> The ADHD time-organizer backend: the app people actually use to plan their day, keep routines and
> to-do lists, track where their time really went, and get nudged about it.

## What it does

The portal is both a **feature module** (activities, planning, to-do lists, routines, timers,
tracking, reminders) and the solution's **composition host** — it owns `AppDbContext`, every
migration, identity/auth wiring, and the DI/Quartz/FastEndpoints composition that pulls the
`Sydowwe.*` modules together.

The domain is built on one noun: an **Activity** (a named thing the user does, owned by a *role* and
optionally a *category*). Everything else points at it — a planner task on a calendar day, a to-do
list item, a routine item on a repeating period, a history record of time actually spent, a timer
preset, a tracker mapping. That is what lets the app say "you planned 2h of Study on Tuesdays and
spent 40 minutes" without joining across unrelated concepts.

Users are single-tenant to themselves: every row belongs to exactly one user and nothing reads across
users. The UI is a separate SPA repo; this project is API-only (FastEndpoints, `/api` prefix).

## Setup / running

See [`../../docs/setup.md`](../../docs/setup.md) for the general setup. Portal-specific notes:

- **Postgres + a `.env` file.** `Env.Load()` runs first in `Program.cs`; connection strings come from
  `config/DatabaseStringsHelper.cs`, and `PAGE_URL` / `EXTENSION_ID` drive the CORS policy.
- **Config precedence is env-over-JSON** — `Program.cs` re-adds `AddEnvironmentVariables()` last on
  purpose. Module secrets use `Section__Key` (e.g. `PushNotification__VapidPrivateKey`).
- **Migrations live here**, including those for module entities that live in the `framework/`
  submodule. `dotnet ef database update` from this project.
- **Materialized views are not migrations.** The three suggestion-pattern views in
  `infrastructure/persistence/sqlScripts/` are installed at boot by `SuggestionPatternViewInstaller`
  (embedded resources, created only when `to_regclass` says they are missing). Without them any save
  touching `PlannerTask` / `ActivityHistory` / `Calendar` fails with 42P01.
- **Nothing seeds on startup.** All four passes in `Program.SeedDatabase` are commented out; seeding
  is run deliberately. Dev passes truncate — never run them outside a dev environment.
- **Culture defaults to `sk-SK`** (supported: `sk-SK`, `en-US`).
- Swagger is development-only, at the usual FastEndpoints route.

## Docs

- `summary.md` — start here if you're working in this project
- `domain-map.md` — model, invariants, business rules, navigation index
- `testing.md` — how to test this portal

Solution-wide conventions (entity bases, endpoint bases, seeding, auth plumbing) are in the root
[`CLAUDE.md`](../../CLAUDE.md); the module docs are under `framework/*/docs/`.
