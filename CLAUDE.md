# AdhdTimeOrganizer

## Read before you edit

Each project documents itself in its own `docs/summary.md` — **read the one for the project you're
about to touch.** It orients you and points to `docs/domain-map.md`, so you open only the files you
need.

Deeper reference, in `docs/claude/` — open the one your task touches:

| File | Covers |
|---|---|
| [slices.md](docs/claude/slices.md) | What lives in each slice project, the cross-slice seams, the `framework/` submodule |
| [persistence.md](docs/claude/persistence.md) | Entity bases, EF configuration helpers, DbContext helpers, query filters, seeding, retention, auditing |
| [endpoints.md](docs/claude/endpoints.md) | FastEndpoints base classes (incl. auth), roles, user scoping, DTO conventions |
| [wiring.md](docs/claude/wiring.md) | Composition root / module DI, scheduling, auth plumbing, config precedence, email templates |
| [testing.md](docs/claude/testing.md) | Test infrastructure and the guards that pin the silent-failure mechanisms |

> The root `docs/extendingVanillaForCustomers.md`, `docs/testing.md` and `docs/architecture.md` are
> leftover copies from the MojaDigitalnaFirma solution and describe modules/types that do **not**
> exist here. Don't trust them. `docs/modules.md` **was** rewritten for this solution and is accurate.

## Solution layout

The portal has been fully split into vertical slices; **the split is finished**.

- `AdhdTimeOrganizer` — the host: remaining feature areas, `AppDbContext`, migrations, DI wiring,
  `Program.cs`, cross-slice relationship declarations. `AdhdTimeOrganizer/reference/mojaCore/` is
  foreign reference code — don't extend it.
- `AdhdTimeOrganizer.Core` — `User`, `Activity`, roles/categories, timer presets, base shims, the
  three shared enums, DTO bases, cross-slice events and seams.
- `AdhdTimeOrganizer.TodoLists` — lists, items, steps, categories, `TaskPriority`, and the shared
  to-do primitives Routines builds on.
- `AdhdTimeOrganizer.History` — `ActivityHistory` and its dashboards.
- `AdhdTimeOrganizer.Planning` — calendar, planner tasks, day templates, **and reminders**.
- `AdhdTimeOrganizer.Routines` — routines, streaks, `StreakOutcome`.
- `AdhdTimeOrganizer.Tracking` — desktop / web-extension / android ingest ledgers and dashboards.
- `AdhdTimeOrganizer.ActivityProfiles` — the three `Activity*Profile` entities, their four lookups,
  `MemoryAnchor`.
- `AdhdTimeOrganizer.IntegrationTests` — stays in the parent; it pins *this host's* composition.
- `framework/` — a **git submodule** holding `Sydowwe.Framework`, `.Contracts`, `.Testing` and the
  opt-in `Sydowwe.Notifications` / `.Reminders` / `.Scheduler` / `.Scheduler.Xlsx` modules.

**Rules that hold everywhere:**

- Every slice references **only Core and the framework**. There is exactly one outbound slice edge in
  the solution — Routines → TodoLists. Before adding a second, read the four inversion patterns in
  [slices.md](docs/claude/slices.md#cross-slice-seams); one of them will fit.
- Slices never see the host, so they take a plain `DbContext`, not `AppDbContext`.
- An enum belongs to the slice that owns it. Core holds exactly three, each with two consumers that
  can't see each other.
- **There is one copy of everything shared** — reach for `Sydowwe.Framework.*` from portal code too.
  The portal/framework reconciliation is finished; if something looks duplicated, it isn't.
- **`framework/` is a submodule: editing it is a two-repo operation.** Commit and push there *first*,
  then commit the new gitlink here. `git status` in the parent shows only "modified: framework (new
  commits)", so an uncommitted framework edit is invisible to the parent's diff and won't travel with
  a push. Module entities live in the submodule; their **migrations live here**.
- **Never anchor an assembly scan** (`ApplyConfigurationsFromAssembly`, `ModuleAssemblies`,
  FastEndpoints `o.Assemblies`) **on a type that can move slices.** It compiles fine and silently
  drops half the model.

## Writing code here

- **Entities** derive from the `Sydowwe.Framework` bases (`BaseEntity`, `BaseTableEntity`,
  `BaseEntityWithUser`, `BaseLookupWithUser`); the two user-scoped ones go through the portal's
  closing shims. See [persistence.md](docs/claude/persistence.md).
- **EF configurations** always use the builder extension helpers — call `BaseEntityConfigure()` first,
  and never hand-roll `ToTable` / `HasKey` / row_version / timestamps.
- **Endpoints**: check `framework/Sydowwe.Framework/application/endpoint/base/` before writing a
  custom one — there is a base for every CRUD, grid, filter/sort and auth pattern, including ones that
  don't follow the naming convention. Convention is `<Verb><Entity>Endpoint`. Mapping lives on the
  DTOs (`ICreateRequest` / `IUpdateRequest` / `IProjectionResponse`), not a mapper generic.
- **Roles**: default `AllowedRoles()` is User + Admin + Root, which is correct for almost everything
  here. Narrow with `IEndpoint.GetAdminRole()` on genuine admin surface. Never hard-code role strings.
- **User scoping is not done by the role gate or the endpoint base.** Portal entities implementing
  `IEntityWithUser` are covered by a global query filter on `AppDbContext`; **module** reads and
  non-`IEntityWithUser` entities have no safety net and must scope by hand. A query filter must read
  the user id off the DbContext (`ScopeUserId`), never off a captured service — see
  [persistence.md](docs/claude/persistence.md#user-scoping-query-filters).
- **Time-of-day** is `TimeDto` in portal DTOs, `MyIntTime` in module DTOs. Don't mix them.
- **Seeders**: pick the kind by owner (app-wide / per-user) × purpose (production / dev fixture); only
  dev seeders truncate. Read `AdhdTimeOrganizer.Core/.../seeder/SeederOrderBands.md` before adding one
  anywhere in the solution.
- **Auditing exists in Framework but is NOT wired up here.** Nothing is written today — don't assume
  CRUD is being captured.

## Logging — no PII at the call site

`PiiRedactor` exists but is **not** wired into this app's Serilog pipeline, so nothing is scrubbed
automatically — and even when wired it only matches structured PII (emails, IBANs, Slovak birth
numbers). Free-text PII cannot be regex-scrubbed. Log files survive GDPR erasure.

**Never put a person's name, address, phone, or email into a log message or its structured
arguments.** Log a stable non-PII identifier instead (entity id, `{UserId}`, correlation id). When an
email genuinely aids diagnostics, pass it through `PiiRedactor.MaskEmail` (`j***@domain`). Logging
entity *type* names, file names and ids is fine.

## Silent failures

Most of the load-bearing mechanisms here break without a build error, an exception or a log line: seam
resolution is by string key, scheduled jobs need a registrar on every boot, query filters get compiled
once and cached, FK constraint names are derived from navigations, and cross-slice event handlers
log-and-swallow. **So a new cross-cutting mechanism needs a test that asserts on rows, not on routes.**
The existing guards are listed in [testing.md](docs/claude/testing.md#things-that-only-a-behavioural-test-catches)
— copy the nearest one's shape.
