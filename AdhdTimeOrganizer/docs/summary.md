# AdhdTimeOrganizer (Portal) — Agent Summary

**Purpose:** The ADHD time-organizer's own feature domain — activities, day planning, to-do lists,
routines, timers, tracking, personal reminders — *and* the composition host that wires
`Sydowwe.Framework` plus the Notifications / Reminders / Scheduler modules into a running app.

**Bounded context:** Owns every portal entity, `AppDbContext` (including the module tables' schema and
**all** migrations), identity/auth composition, and the HTTP surface. Does **not** own: the framework
primitives (base entities, base endpoints, seeder machinery, auth flows) or the module domains
(notification delivery, reminder scheduling, job scheduling) — those live in the `framework/`
submodule and are consumed through `Sydowwe.Framework.Contracts`.

## Dependency seams

- **Consumes (contracts, `Sydowwe.Framework.Contracts`):** `INotificationService` (routine
  notifications), `IReminderRegistry` (personal reminders), `IScheduler` (module job registration),
  `ISubjectDataEraser` (account-deletion fan-out).
- **Consumes (framework):** every entity base, endpoint base, `DbContextHelper`, seeder interfaces
  and managers, `PasswordSignInFlow` / `UserRegistrationFlow`, `RefreshTokenCleanupService`.
- **Exposes to the framework/modules:** `User` (the concrete `TUser`), `IUserDefaultsService`
  (`UserDefaultsService`), `ISeedUserProvider` (`SeedUserIdProvider`),
  `IAdditionalUserClaimsProvider<User>` (`ExtensionRoleClaimsProvider`), and `DbContext` →
  `AppDbContext` aliasing so host-agnostic module services can activate.
- **Exposes to clients:** ~275 FastEndpoints classes under `application/endpoint/`, all under `/api`.
  Consumers are the SPA (separate repo), a Chrome extension, a desktop tracker and an Android app.
- **External:** Google Sign-In and Google Calendar (`infrastructure/extService/googleCalendar/`,
  `.../user/auth/`) — both deliberately portal-only, never moved to Framework.

## Gotchas — things that will bite you

- **Per-user scoping is done by the DbContext, not by endpoints.** `UserScopingOptions.Enabled` is
  turned **on** in `Program.cs` (Framework defaults it off), so `BaseDbContext` applies a global query
  filter to every `IEntityWithUser`. `ApplyUserScoping` on the grid/filter bases is a no-op. Two
  consequences: (1) `ActivityBacklogProfile` / `ActivityProjectProfile` / `ActivityBucketListProfile`
  are **not** `IEntityWithUser` and get no filter — their grids scope by hand with
  `p.Activity.UserId == userId`; (2) module reads have no filter at all.
- **`WebExtensionActivityEntry` is excluded from the automatic filter** and carries a hand-written
  combined one (`RecordDate >= CurrentPartitionDate && user check`) in `AppDbContext.OnModelCreating`.
  `CurrentPartitionDate` is *today minus two years* — rows older than that are invisible to every
  query, by design (the table is RANGE-partitioned on `record_date`, as is `DesktopActivityEntry`).
- **A background job runs unauthenticated, which is exactly what makes the sweep work** (the filter is
  inert when `IsAuthenticated` is false). The flip side: a background *insert* of a
  `BaseEntityWithUser` gets `UserId == 0` and dies on the FK — set `UserId` explicitly in jobs and
  event handlers.
- **Saving `PlannerTask`, `ActivityHistory` or `Calendar` marks the matching pattern view dirty.**
  `SuggestionPatternRefreshInterceptor` no longer runs `REFRESH MATERIALIZED VIEW CONCURRENTLY`
  itself — it queues the view name in the singleton `ISuggestionPatternRefreshQueue`, and
  `SuggestionPatternRefreshJobHandler` (a keyed `IScheduledJobHandler` on the Scheduler module's
  substrate, every 10s, `DisallowConcurrent`) drains the queue
  and does the actual refresh off the request thread, coalescing several saves in the same window into
  one refresh per view. A refresh failure (42P01 if the view is missing — hence
  `SuggestionPatternViewInstaller` at boot and the fixture's `OnSchemaCreatedAsync` in tests) is logged
  by the job and no longer surfaces to the request that triggered it. Don't add a fourth pattern view
  without adding the SQL script, a `SuggestionPatternView` enum member, and the job's `ViewNames` entry.
- **Reminder times are derived, not authored.** For a reminder attached to a planner task,
  `Reminder.RemindAt` is recomputed by `ReminderRegistrationService.SyncAsync` from
  `Calendar.Date + PlannerTask.StartTime` **in the user's own time zone**. Anything that moves a task
  must re-sync (`SyncForPlannerTasksAsync`) and anything that deletes tasks must cancel the orphaned
  registrations (`GetReminderIdsForPlannerTasksAsync` **before** the save, `CancelManyAsync` after) —
  `ApplyTemplatePlannerTaskEndpoint` is the worked example. Quiet hours, by contrast, are evaluated by
  Notifications in the single deployment zone, so the two diverge for a travelling user.
- **Deleting a planner task cascades its reminders in the DB**, but the module-side
  `ReminderDefinition` is *not* FK'd to anything here — it only goes away if someone calls
  `CancelAsync`. Cascade + silence = a definition scanning forever.
- **`RoutineTimePeriod` reset instants are computed, never stored.** `RoutineResetService.ComputeNextReset`
  re-derives from `LastResetAt` + `LengthInDays` + `ResetAnchorDay` on every call, which is *why* the
  idempotency marks (`EndingSoonNotifiedFor`, `GraceNotifiedFor`) compare against a freshly computed
  instant rather than a bool. Change the anchor and the period correctly earns a fresh nudge.
- **Every scheduled job in the solution now runs on the Scheduler module's substrate.** `Program.cs`
  makes exactly one scheduling call — `services.AddSchedulerSubstrate()` — plus one
  `AddHostedService<…ScheduledJobsRegistrar>()` per owner (Notifications, Reminders, Scheduler, Routines,
  Tracking, Portal). `Sydowwe.Scheduler` is the only project that references Quartz; nobody else writes
  an `IJob`. The routine reset (02:00) and nudge (09:00) are now
  `Routines.TodoListReset` / `Routines.PeriodNudge` handlers registered by
  `RoutinesScheduledJobsRegistrar`.
- **Notification dispatch from the routine domain is best-effort and deliberately post-commit.**
  `RoutinePeriodNotificationService` swallows exceptions and logs the period **id** only — `Text` is
  user-authored, so it stays out of logs (no-PII rule).
- **Auditing is not wired up here.** `AppDbContext.ApplyFrameworkConfigurations` maps only
  `BusinessAuditLog` and explicitly `Ignore<AuditLog>()`s the rest; `AuditSaveChangesInterceptor` is
  not registered. Only explicit `IAuditService.LogAsync` calls write anything.
- **FastEndpoints discovery is an explicit four-assembly list** (`Program.cs`, with
  `DisableAutoDiscovery = true`). A new module's endpoints 404 until it's added. Extension clients are
  **deny-by-default** via a per-endpoint policy in the endpoint configurator — not a fallback policy,
  which would never be reached.
- **Swagger has one live workaround, and its registration order is load-bearing.**
  `RemoveToEntitySchemaProcessor` strips `ICreateRequest<TEntity>.ToEntity`, which otherwise pulls the
  cyclic EF nav graph into the schema. It is registered with `PrependTo(...)`, **not** `Add(...)`:
  FastEndpoints adds its own `ValidationSchemaProcessor` inside `EnableFastEndpoints` and only then
  invokes the host's `DocumentSettings` action, so appending runs the stripper too late and
  `/swagger/v1/swagger.json` kills the process with an uncatchable `StackOverflowException`. That was
  mistaken for an upstream FastEndpoints bug for a while (CQ-33); it is not, and no package bump is
  needed. Pinned by `SwaggerSchemaProcessorOrderTests`.
- **Request bodies are logged.** `UseSerilogRequestLogging` buffers and logs up to 1000 chars of every
  non-GET body. That is a PII surface — do not add endpoints that take names/addresses in the body
  without revisiting it.
- **`Activity.Clone()` is a `MemberwiseClone` with `Id = 0`** — it copies navigation *references* too.
  `CloneActivityEndpoint` is the only caller; treat it as a shallow copy.

## Extension playbook

- **Add a portal entity:** 1) class under `domain/model/entity/<area>/`, deriving from the portal's
  closing shim `BaseEntityWithUser` (or `BaseLookupWithUser` for lookups); 2) configuration under
  `infrastructure/persistence/configuration/<area>/` calling `BaseEntityConfigure<T>()` first and
  `IsManyWithOneUser` / `IsOneWithOneUser` for the user FK; 3) `DbSet` on `AppDbContext`;
  4) `dotnet ef migrations add`; 5) endpoints (below); 6) if it is per-user default data, a
  `…Seeder : IPerUserDefaultSeeder` under `infrastructure/persistence/seeder/userDefault/`.
- **Add an endpoint:** pick the matching base from `framework/Sydowwe.Framework/application/endpoint/base/`
  (see the table in root `CLAUDE.md`), name it `<Verb><Entity>Endpoint`, put it under
  `application/endpoint/<area>/<entity>/{command|query}/`. Mapping goes on the DTOs
  (`ICreateRequest<T>.ToEntity`, `IUpdateRequest<T>.UpdateEntity`, `IProjectionResponse<TRes,TEntity>.Projection`),
  never a mapper. A `FluentValidation` validator goes in `application/validator/`. Registration is
  automatic (assembly is already in `o.Assemblies`); the role gate defaults to User-or-higher.
- **Add a scheduled portal job:** an `IScheduledJobHandler` + `IScopedService` in
  `infrastructure/jobs/` (a `HandlerKey` const, inject the scoped services you need directly — the
  dispatcher opens the scope per fire), then a `RecurringJobRegistration` in
  `infrastructure/scheduling/PortalScheduledJobsRegistrar.cs` carrying the cadence and
  `DisallowConcurrent`. **Never** add a Quartz `IJob` or a second `AddQuartz` call — the substrate is
  `services.AddSchedulerSubstrate()` and Quartz belongs to `Sydowwe.Scheduler` alone. Remember the
  unauthenticated-insert trap above.
- **Add a routine/reminder notification:** don't call `INotificationService` directly from a job or
  endpoint — go through `IRoutinePeriodNotificationService` / `IReminderRegistrationService`. They are
  the only classes here that know the Contracts payload shapes, and keeping that true is what stops
  payload/type drift.
- **Add a cross-entity side effect on completion:** the `IsDone` fan-out already exists as
  FastEndpoints events (`application/event/` + `application/eventHandler/`). Publish an event rather
  than reaching into another aggregate from an endpoint.

## Deeper reference

- `domain-map.md` — model, invariants, business rules, navigation index
- `testing.md` — test strategy and known gaps
- Root [`CLAUDE.md`](../../CLAUDE.md) — solution-wide conventions (entity/endpoint bases, seeding,
  auth plumbing, composition root)
- Module docs: `framework/Sydowwe.Notifications/docs/`, `framework/Sydowwe.Reminders/docs/`,
  `framework/Sydowwe.Scheduler/docs/`, `framework/Sydowwe.Framework/docs/`
