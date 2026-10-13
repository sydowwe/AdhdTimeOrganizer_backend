# AdhdTimeOrganizer (Portal) — Agent Summary

**Purpose:** The composition host. It wires `Sydowwe.Framework`, the seven feature slices and the
Notifications / Reminders / Scheduler modules into a running app — and owns the one feature area that
never left: users and auth.

**⚠ The vertical split is finished, and this project no longer owns a feature domain.** Activities,
day planning, to-do lists, routines, history, tracking and the activity profiles are each their own
project (see [`docs/modules.md`](../../docs/modules.md)). `AdhdTimeOrganizer/domain/` holds two
external-service interfaces and no entities at all. If you came here looking for a feature, you are in
the wrong project — but if you came looking for *why a feature's wiring doesn't fire*, you are in the
right one.

**Bounded context:** Owns

- **Users and auth** — password login, 2FA, email confirmation and change, forgot/reset password,
  refresh tokens and sessions, extension auth, user preferences, account deletion and the GDPR data
  export (36 endpoints under `application/endpoint/{user,calendar}/`).
- **Coming back after a gap (FO5)** — `User.LastActiveAt`, stamped hourly by `LastActiveStampMiddleware`,
  and the welcome-back card: `GET /welcome-back`, `POST /welcome-back/{fresh-start,fresh-start/undo,dismiss}`
  (`application/endpoint/user/welcomeBack/`). The pile is defined once in
  `application/service/welcomeBack/WelcomeBackPile.cs` and reads TodoLists, Planning, Routines and
  Notifications directly, for the reason `GetAccountDeletionSummaryEndpoint` gives. The thresholds are
  Core's `UserPresence`. Each broken routine carries `canFreeze`, Routines' `DescribeBreak` over its whole
  history, so the card points to a skip only where the freeze endpoint would grant one. **The back-off:** `RecipientEngagementReader` (same folder) is the framework's
  `IRecipientEngagementReader`, and the catalog in `ModuleServiceExtensions` lists the four routine kinds
  as `HeldWhileAwayTypes`. A user away three days or more gets those in the bell only, never pushed or
  mailed; their own reminders, the timer alarm and the scheduler alerts still go out.
- **The morning briefing (P2-01)** — one notification per user per morning instead of a push per signal:
  today's open blocks and the first one ahead, the carry-over week, date-only to-dos due today, overdue
  to-dos, and the routine items suggested for today. `infrastructure/jobs/MorningBriefingJobHandler.cs`
  (every 15 min, registered by `PortalScheduledJobsRegistrar`) decides *when* — the day's wake-up plus
  `User.MorningBriefingOffsetMinutes`, once per local date via `User.MorningBriefingHandledFor`;
  `application/service/morningBriefing/MorningBriefingComposer.cs` decides *what*, reading Planning, TodoLists
  and Routines directly and calling their rules (`PlannerTaskCarryOverRule`, `RoutineSuggestedDay`). No
  briefing on an empty morning, an away day, or while the welcome-back card is open; held while away; email
  off by default. `User.MorningBriefingEnabled` (Core, default on) is what makes Routines' own suggested-today
  digest step aside. Pinned by `MorningBriefingJobTests`.
- **Sign-ups that never confirmed (P2-05)** — `RequireConfirmedEmail` means such an account can never sign
  in, so it is reminded once and then deleted. `infrastructure/jobs/UnconfirmedAccountJobHandler.cs` (hourly,
  `PortalScheduledJobsRegistrar`) resends the confirmation link a day after `CreatedTimestamp`, marking
  `User.ConfirmationReminderSentAt`, and deletes the account after 14 days. Confirmed accounts, Google sign-ins
  and the seeded root admin are never touched. **Deletion has one path:**
  `application/service/user/UserAccountDeletionService.cs` runs the `ISubjectDataEraser` fan-out and
  `UserManager.DeleteAsync` for both this purge and `DeleteUserAccountEndpoint` (which uses only its staging half,
  because the framework base owns the delete). Pinned by `UnconfirmedAccountJobTests`, which checks every
  user-scoped table in the model through `UserDataInventory`.
- **The Google integrations** — Sign-In and Calendar sync (`infrastructure/extService/`), deliberately
  portal-only.
- **The composition root** — `AppDbContext` (framework + every slice's configurations, the global
  query filters, and `ConfigureCrossSliceRelationships`, where FKs whose two ends live in slices that
  cannot see each other are declared), `ModuleSchemas`, **every migration in the solution** (module
  entities live in the submodule; their migrations live here), `Program.cs`, the DI / Quartz /
  FastEndpoints wiring, the suggestion-pattern machinery (view installer, interceptor, queue, refresh
  job), `PortalScheduledJobsRegistrar`, and the two cross-slice event handlers.

Does **not** own: any feature entity; the framework primitives (base entities, base endpoints, seeder
machinery, auth flows); or the module domains (notification delivery, reminder scheduling, job
scheduling) — those live in the `framework/` submodule and are consumed through
`Sydowwe.Framework.Contracts`.

## Dependency seams

- **Consumes (contracts, `Sydowwe.Framework.Contracts`):** `INotificationService` (routine
  notifications), `IReminderRegistry` (personal reminders), `IScheduler` (module job registration),
  `ISubjectDataEraser` (account-deletion fan-out).
- **Consumes (framework):** every entity base, endpoint base, `DbContextHelper`, seeder interfaces
  and managers, `PasswordSignInFlow` / `UserRegistrationFlow`, `IRefreshTokenService` (its cleanup runs as
  the `RefreshTokenCleanupJobHandler` Scheduler job).
- **Exposes to the framework/modules:** `User` (the concrete `TUser`), `IUserDefaultsService`
  (`UserDefaultsService`), `ISeedUserProvider` (`SeedUserIdProvider`),
  `IAdditionalUserClaimsProvider<User>` (`ExtensionRoleClaimsProvider`), and `DbContext` →
  `AppDbContext` aliasing so host-agnostic module services can activate.
- **Consumes (slices):** all seven, and only to *register* them — every slice's assembly appears in
  FastEndpoints' `o.Assemblies`, `ModuleAssemblies` and `ApplyHostConfigurations`. The host also names
  slice entity types in `AppDbContext.ConfigureCrossSliceRelationships` and `ModuleSchemas`. This is
  the only direction that edge runs: **no slice references the host.**
- **Exposes to clients:** ~300 FastEndpoints classes, all under `/api` — but only **40** of them live
  in this project (`application/endpoint/`); the rest are the slices', registered here. Consumers are
  the SPA (separate repo), a Chrome extension, a desktop tracker and an Android app.
- **External:** Google Sign-In and Google Calendar (`infrastructure/extService/googleCalendar/`,
  `.../user/auth/`) — both deliberately portal-only, never moved to Framework.

## Gotchas — things that will bite you

- **Per-user scoping is done by the DbContext, not by endpoints.** `UserScopingOptions.Enabled` is
  turned **on** in `Program.cs` (Framework defaults it off), so `BaseDbContext` applies a global query
  filter to every `IEntityWithUser`. `ApplyUserScoping` on the grid/filter bases is a no-op. Three
  consequences: (1) `ActivityBacklogProfile` / `ActivityProjectProfile` / `ActivityBucketListProfile`
  (now in `AdhdTimeOrganizer.ActivityProfiles`) are **not** `IEntityWithUser` and get no filter —
  their grids scope by hand with `p.Activity.UserId == userId`; (2) module reads have no filter at
  all; (3) **the filter is declared here but relied on there** — a slice's endpoints inherit their
  scoping from this project, so a change to the filter set is a solution-wide change with no
  compile-time trace in the slices it protects.
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
  itself — it queues the view name in the singleton `ISuggestionPatternRefreshQueue` (a change of a
  user's `Timezone` queues the history view too, since that view reads `user.timezone`), and
  `SuggestionPatternRefreshJobHandler` (a keyed `IScheduledJobHandler` on the Scheduler module's
  substrate, every 10s, `DisallowConcurrent`) drains the queue
  and does the actual refresh off the request thread, coalescing several saves in the same window into
  one refresh per view. A refresh failure (42P01 if the view is missing — hence
  `SuggestionPatternViewInstaller` at boot and the fixture's `OnSchemaCreatedAsync` in tests) is logged
  by the job and no longer surfaces to the request that triggered it. Don't add a fourth pattern view
  without adding the SQL script, a `SuggestionPatternView` enum member, and the job's `ViewNames` entry.
- **Reminder times are derived, not authored.** (`ReminderRegistrationService` lives in
  `AdhdTimeOrganizer.Planning` now, along with the reminders themselves; the rule is still worth
  knowing here because the cascade below is declared in `AppDbContext`.) For a reminder attached to a
  planner task,
  `Reminder.RemindAt` is recomputed by `ReminderRegistrationService.SyncAsync` from
  `Calendar.Date + PlannerTask.StartTime` **in the user's own time zone**. Anything that moves a task
  must re-sync (`SyncForPlannerTasksAsync`) and anything that deletes tasks must cancel the orphaned
  registrations (`GetReminderIdsForPlannerTasksAsync` **before** the save, `CancelManyAsync` after) —
  `ApplyTemplatePlannerTaskEndpoint` is the worked example. Quiet hours, by contrast, are evaluated by
  Notifications in the single deployment zone, so the two diverge for a travelling user.
- **Deleting a planner task cascades its reminders in the DB**, but the module-side
  `ReminderDefinition` is *not* FK'd to anything here — it only goes away if someone calls
  `CancelAsync`. Cascade + silence = a definition scanning forever.
- **`RoutineTimePeriod` reset instants are computed, never stored.** (`AdhdTimeOrganizer.Routines`
  owns this; it stays listed here because the two sweeps are registered through the substrate this
  project composes.) `RoutineResetService.ComputeNextReset`
  re-derives from `LastResetAt` + `LengthInDays` + `ResetAnchorDay` on every call, which is *why* the
  idempotency marks (`EndingSoonNotifiedFor`, `GraceNotifiedFor`) compare against a freshly computed
  instant rather than a bool. Change the anchor and the period correctly earns a fresh nudge.
- **Every scheduled job in the solution now runs on the Scheduler module's substrate.** `Program.cs`
  makes exactly one scheduling call — `services.AddSchedulerSubstrate()` — plus one
  `AddHostedService<…ScheduledJobsRegistrar>()` per owner (Notifications, Reminders, Scheduler, Routines,
  Tracking, Portal). `Sydowwe.Scheduler` is the only project that references Quartz; nobody else writes
  an `IJob`. The routine reset (02:00 UTC) and nudge (every 15 min) are now
  `Routines.TodoListReset` / `Routines.PeriodNudge` handlers registered by
  `RoutinesScheduledJobsRegistrar`.
- **Notification dispatch from the routine domain is best-effort and deliberately post-commit.**
  `RoutinePeriodNotificationService` (in `AdhdTimeOrganizer.Routines`) swallows exceptions and logs
  the period **id** only — `Text` is user-authored, so it stays out of logs (no-PII rule).
- **Auditing is not wired up here.** `AppDbContext.ApplyFrameworkConfigurations` maps only
  `BusinessAuditLog` and explicitly `Ignore<AuditLog>()`s the rest; `AuditSaveChangesInterceptor` is
  not registered. Only explicit `IAuditService.LogAsync` calls write anything.
- **FastEndpoints discovery is an explicit eleven-assembly list** (`Program.cs`, with
  `DisableAutoDiscovery = true`): this project, the seven slices, and the three opt-in modules. A new
  slice's endpoints 404 until it is added — no build error, no log line. **Anchor each entry on a type
  that cannot move slices** (an entity, not a configuration or an endpoint); the same rule governs
  `ModuleAssemblies` and `ApplyHostConfigurations`. Extension clients are
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
- **Every authenticated web request may write to `user.user`** (FO5). `LastActiveStampMiddleware` runs
  between `UseAuthorization` and `UseFastEndpoints`, at most once per user per hour (an in-process
  `IMemoryCache` throttle plus a `WHERE last_active_at < now - 1h`), and tracker tokens never stamp. It
  must stay **before** the endpoints: the request that ends an absence opens the welcome-back card
  (`WelcomeBackAwayFrom` / `WelcomeBackReturnedAt`) in the same `UPDATE` that overwrites `LastActiveAt`,
  so the card no longer depends on which of the client's first requests reads it. All three columns are
  written with `ExecuteUpdateAsync` or as tracked single-property changes, never through
  `UserManager.UpdateAsync`, which rewrites every column. `LastActiveStampTests` pins the tracker
  exclusion; `WelcomeBackTests` pins the rest on rows.
- **The notification back-off's reader is an optional dependency** (FO5). The framework dispatcher takes
  `IRecipientEngagementReader` with a `null` default, so dropping its line in `ModuleServiceExtensions`
  fails nowhere: nobody is ever away and the routine nudges keep reaching people who stopped opening the
  app. `ModuleWiringTests` pins the registration; `NotificationAwayHoldTests` pins the hold on what the
  fake senders receive, for the live send and the quiet-hours flush.

## Extension playbook

- **Add a feature entity — in the owning slice, not here.** 1) class under that project's
  `domain/model/entity/`, deriving from Core's closing shim `BaseEntityWithUser` (or
  `BaseLookupWithUser`); 2) configuration in the slice, calling `BaseEntityConfigure<T>()` first and
  `IsManyWithOneUser` / `IsOneWithOneUser` for the user FK — **never** a hand-rolled `ToTable`, and
  never a schema (the finalizing convention applies it from `ModuleSchemas`); 3) **no `DbSet`** —
  slices take a plain `DbContext` and use `dbContext.Set<T>()`; 4) `dotnet ef migrations add` **from
  here**, because every migration lives in this project; 5) endpoints (below); 6) a seeder if it is
  default data, in the slice's own order band.
  Three things in *this* project may still need touching, none of which fail the build: a new slice
  must be added to `ModuleSchemas` (this one throws at model build, loudly), to FastEndpoints'
  `o.Assemblies` + `ModuleAssemblies` + `ApplyHostConfigurations`, and — if the entity is user-scoped
  — classified by `UserDataExportAndDeletionTests`.
- **Add an FK whose two ends are in different slices:** it cannot be configured in either one. Declare
  it in `AppDbContext.ConfigureCrossSliceRelationships`, navigation-free, with a **pinned constraint
  name** (names are otherwise derived from navigations and shift with assembly ordering) and a
  deliberate delete behaviour. `ActivityHistory.TodoListItemId` is the worked example: `SetNull`,
  because deleting a task must not delete the record that time was spent on it.
- **Add an endpoint:** pick the matching base from `framework/Sydowwe.Framework/application/endpoint/base/`
  (see the table in root `CLAUDE.md`), name it `<Verb><Entity>Endpoint`, and put it in **the slice that
  owns the entity** — under `application/endpoint/<entity>/{command|query}/` there. Mapping goes on the
  DTOs (`ICreateRequest<T>.ToEntity`, `IUpdateRequest<T>.UpdateEntity`,
  `IProjectionResponse<TRes,TEntity>.Projection`), never a mapper. Registration is automatic *provided
  the slice's assembly is already in `o.Assemblies`*; the role gate defaults to User-or-higher. Only
  auth, user-settings and the Google integrations belong in this project's `application/endpoint/`.
- **Reach another slice's rows:** you cannot add a project reference — every slice sees only Core and
  the framework, and the solution has exactly one outbound slice edge (Routines → TodoLists). Declare
  a seam in `AdhdTimeOrganizer.Core/application/seam/` instead and read its `README.md`, which is the
  registry of every seam, who implements it, and how each one fails.
- **Add a scheduled portal job:** an `IScheduledJobHandler` + `IScopedService` in
  `infrastructure/jobs/` (a `HandlerKey` const, inject the scoped services you need directly — the
  dispatcher opens the scope per fire), then a `RecurringJobRegistration` in
  `infrastructure/scheduling/PortalScheduledJobsRegistrar.cs` carrying the cadence and
  `DisallowConcurrent`. **Never** add a Quartz `IJob` or a second `AddQuartz` call — the substrate is
  `services.AddSchedulerSubstrate()` and Quartz belongs to `Sydowwe.Scheduler` alone. Remember the
  unauthenticated-insert trap above.
- **Add a routine/reminder notification:** don't call `INotificationService` directly from a job or
  endpoint — go through `IRoutinePeriodNotificationService` (`AdhdTimeOrganizer.Routines`) or
  `IReminderRegistrationService` (`AdhdTimeOrganizer.Planning`). They are the only classes that know
  the Contracts payload shapes, and keeping that true is what stops payload/type drift. A slice that
  needs its own one-shot alarm follows `History.TimerBoundaryAlarmService` instead — plus a boot
  registrar, because the Quartz job store is in-memory and loses every trigger on restart.
- **Add a cross-entity side effect on completion:** the `IsDone` fan-out exists as Core `ISeamEvent`s.
  Publish one rather than reaching into another aggregate from an endpoint. The handlers live in the
  slice that owns the *reacting* entity; the two that react to no slice in particular
  (`ActivityTimeRecordedEventHandler`, `PlannerTaskIsDoneChangedEventHandler`) are in this project's
  `application/eventHandler/`. ⚠ Handlers **log and swallow**, so a broken one is invisible — pair it
  with a test that asserts on rows.

## Deeper reference

- `domain-map.md` — model, invariants, business rules, navigation index
- `testing.md` — test strategy and known gaps
- Root [`CLAUDE.md`](../../CLAUDE.md) — solution-wide conventions (entity/endpoint bases, seeding,
  auth plumbing, composition root)
- Module docs: `framework/Sydowwe.Notifications/docs/`, `framework/Sydowwe.Reminders/docs/`,
  `framework/Sydowwe.Scheduler/docs/`, `framework/Sydowwe.Framework/docs/`
