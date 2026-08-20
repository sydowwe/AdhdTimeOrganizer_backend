# Entities, Configuration, Seeding & Retention

## Entity base hierarchy

**Framework-only** — `framework/Sydowwe.Framework/domain/entity/`, with the marker interfaces in
`framework/Sydowwe.Framework/domain/entityInterface/`. The portal keeps no copies, only two closing
shims. Portal and module entities alike derive from:

- `base/BaseEntity.cs` — `long Id` only (implements `IEntityWithId`), for SQL views / materialized
  views.
- `base/BaseTableEntity.cs` — adds `CreatedTimestamp` / `ModifiedTimestamp`. Stamped automatically by
  the `SaveChangesAsync()` override (which calls `BaseSaveChangesAsync()`), and also given a `now()`
  DB default. Tables get a `row_version` concurrency token via `BaseEntityConfigure()`.
- `user/BaseEntityWithUser.cs` (+ `user/IEntityWithUser.cs`) — generic over `TUser`, adds `UserId` /
  `User`. The `UserId` FK is **NOT NULL** (enforced when configured via `IsManyWithOneUser` /
  `IsOneWithOneUser`). Filled by `UserDbContextExtensions.BaseWithUserEntitySaveChangesAsync` on
  insert when an authenticated user is present; background inserts without an authenticated user get
  `UserId == 0` and fail with an FK violation.
- `base/BaseLookupWithUser.cs` — `BaseEntityWithUser<TUser>` + `IBaseLookupEntity`.

**The portal's two closing types.** C# can't infer `TUser` from a constraint, so the portal closes the
two user-scoped bases over its own `User` and every entity declaration names the shim, not the generic:

- `domain/model/entity/user/BaseEntityWithUser.cs` → `BaseEntityWithUser<User>`
- `domain/model/entity/base/core/BaseLookupWithUser.cs` → `BaseLookupWithUser<User>`

Keep them plain closing types — behaviour belongs in Framework. `domain/model/entityInterface/` holds
only two portal-specific markers (`IEntityWithIsDone`, `IEntityWithDoneAndTotalCount`); the `IBase*Entity`
family is Framework's.

## Entity configuration

Always use the builder extension helpers — don't hand-roll `ToTable` / `HasKey` / row_version /
timestamps.

**Portal** — `AdhdTimeOrganizer/infrastructure/persistence/configuration/extensions/` — three files,
each holding only what is tied to a portal type:

- `EntityWithUserBuilderExtensions.cs` — `IsManyWithOneUser<TEntity>(navigationProperty?,
  deleteBehavior = Cascade)` and `IsOneWithOneUser<TEntity>(…)`, and nothing else. They survive
  because they name the portal's concrete `User`.
- `EntityWithActivityBuilderExtensions.cs` — `IsManyWithOneActivity<TEntity>()` /
  `IsOneWithOneActivity<TEntity>()` for `BaseEntityWithActivity`.
- `TodoListEntityConfigurationExtensions.cs` — `BaseTodoListConfigure<TEntity>()` for `BaseTodoListItem`.

**Shared — `Sydowwe.Framework`, used by portal *and* module code alike.** Portal configurations
`using Sydowwe.Framework.infrastructure.persistence.configuration.extensions`.

- `configuration/extensions/EntityBuilderExtensions.cs` — `BaseEntityConfigure<TEntity>()` (call
  first: snake_case table name, serial `Id` PK, `row_version` concurrency token, `CreatedTimestamp` /
  `ModifiedTimestamp` defaults), `EnumColumn()` (enum as string), `FlagsEnumColumn()` (`[Flags]` enum
  as `int`) — both column helpers have nullable overloads — plus:
  - `PriceColumn(x => x.Prop, isRequired = true)` — `decimal(18,2)`.
  - `StoredComputedColumn(x => x.Prop, sql)` — Postgres `GENERATED ALWAYS AS (…) STORED`.
  - `EncryptedColumn(x => x.Prop)` — AES-256-GCM at-rest encryption (GDPR Art. 32) for
    high-sensitivity strings. Stores a versioned token in a `text` column; randomized, so the column
    **cannot** be filtered/sorted/uniqued — use only for fields read by row id. Key comes from the
    `FIELD_ENCRYPTION_KEY` env var (base64, 32 bytes; in `.env`, never the repo). See
    `framework/Sydowwe.Framework/infrastructure/persistence/encryption/`. In use on
    `User.GoogleCalendarRefreshToken` via the nullable-property variant `EncryptedColumnNullable`
    (plain `EncryptedColumn` binds `Expression<Func<TEntity, string>>`, which a `string?` property
    can't satisfy). `FIELD_ENCRYPTION_KEY` is a hard boot requirement wherever it's configured — the
    encryptor is constructed inside `OnModelCreating` and throws if the var is unset.

  ⚠ **Table-name gotcha in `BaseEntityConfigure`:** it derives the table name with
  `.Replace("Read", "")` on the *whole* class name, not a suffix strip. No entity in this solution
  contains `Read` — but a future `ReadingLog` / `ThreadState` would silently map to `ing_log` /
  `th_state`. Give such an entity an explicit `ToTable(...)`.

- `configuration/extensions/NameTextColorEntityConfigurationExtension.cs` — the name/text/color/icon
  base-entity helpers: `BaseNameTextEntityConfigure`, `BaseTextColorEntityConfigure`,
  `BaseTextColorIconEntityConfigure`, `BaseNameTextColorEntityConfigure`,
  `BaseNameTextColorIconEntityConfigure`. Each is constrained to the matching `IBase…Entity` marker
  and calls `BaseEntityConfigure()` for you. Note the **file name doesn't match the class prefix** —
  that is why a filename-based sweep once missed these.

- `infrastructure/persistence/PartitioningExtensions.cs` — note the different path, **not** under
  `configuration/extensions/`. `IsPartitionedByRange("Column", partitions)`. In use on
  `DesktopActivityEntry` and `WebExtensionActivityEntry`. Partition SQL is emitted by
  `PartitionedNpgsqlMigrationsSqlGenerator`, wired via `optionsBuilder.ReplaceService<IMigrationsSqlGenerator, …>()`
  in **both** `Program.cs` and `config/AppCommandDbContextFactory.cs` (the design-time factory). New
  partitioned tables need nothing beyond `IsPartitionedByRange`.

## Database schemas — one per module

Every module's tables live in their own Postgres schema. `public` holds almost nothing.

| schema | tables |
|---|---|
| `public` | `__EFMigrationsHistory`, `timer_preset`, `pomodoro_timer_preset` |
| `user` | `user`, `refresh_token`, `AspNetRoles`, and the five Identity satellites |
| `activity` | `activity`, `activity_category`, `activity_role` |
| `todo` | `AdhdTimeOrganizer.TodoLists` (4) |
| `history` | `AdhdTimeOrganizer.History` (1) |
| `planning` | `AdhdTimeOrganizer.Planning` (8) **and the three `mv_*` materialized views** |
| `routines` | `AdhdTimeOrganizer.Routines` (4) |
| `tracking` | `AdhdTimeOrganizer.Tracking` (5, two partitioned) |
| `activity_profiles` | `AdhdTimeOrganizer.ActivityProfiles` (9) |
| `notifications` / `reminders` / `scheduler` | the three framework modules |
| `audit` | `business_audit_log` — the one entity that names its own schema, unchanged |

**You do not pass a schema to `ToTable`.** Table names come from `BaseEntityConfigure`, which knows
nothing about modules; the schema is applied afterwards in one sweep by `SchemaPerModuleConvention`
(`framework/Sydowwe.Framework/infrastructure/persistence/`), from the map in
`AdhdTimeOrganizer/infrastructure/persistence/ModuleSchemas.cs`. A new slice adds one line to that
map — **its entities have no schema until it does, and the model build throws** rather than quietly
putting them in `public`.

Three things about that convention are load-bearing, and each was a real failure while it was written:

- It is an `IModelFinalizingConvention`, not a call at the end of `OnModelCreating`. Mid-
  `OnModelCreating` the model still holds candidate entity types EF has not pruned — `TimeZoneInfo`,
  discovered from `BaseUser.Timezone` before its value converter is applied — and the sweep tries to
  place a schema on them. It also makes the result immune to the order of the
  `ApplyConfigurationsFromAssembly` calls, which keeps shifting as slices move.
- It writes through the **mutable** metadata API. A convention-source write cannot overwrite what
  `ToTable` already recorded at `Explicit`, and `BaseEntityConfigure` calls `ToTable` for every entity
  in the solution — so a convention-source write is silently dropped for all of them.
- "Already has an explicit schema" is decided by the annotation's **value**, not its configuration
  source. `ToTable(name)` — the single-argument overload — writes a *null* schema at `Explicit`, so a
  configuration-source check treats every entity as hand-placed and skips the lot.

Owned types take their **owner's** schema, not the one their own CLR type maps to. `TodoListStep`
lives in TodoLists but is owned by both `todo_list_item` (→ `todo`) and `routine_todo_list` (→
`routines`); resolving it by assembly puts its table and schema in disagreement and fails the model
build.

Cross-schema FKs are ordinary in Postgres and constraint/index names carry no schema, so none of the
pinned `HasConstraintName` values changed. EF-generated SQL is always fully qualified. **Hand-written
SQL is not** — anything using `TRUNCATE`, `REFRESH MATERIALIZED VIEW`, `to_regclass` or
`information_schema` must name the schema, or it silently resolves against the `search_path`. Where
the relation is in the EF model, derive the schema from it (`GetSchema()`, or `GetViewSchema()` for a
`ToView` entity) rather than writing the name down a second time — that is what the seeder truncate
helpers, `SuggestionPatternViewInstaller` and `SuggestionPatternRefreshJobHandler` all do, so the SQL
cannot drift from the mapping. The `mv_*.sql` scripts are the exception and name their schemas
literally; nothing in a script is visible to the model.

`ModuleSchemaTests` pins the whole layout: every table and the three materialized views, read back
out of `information_schema` / `pg_matviews` after the schema is created, plus a truncate through
`TruncateTableCascadeAsync` (the seeders' raw SQL is otherwise unreachable from tests — they run only
behind `Seeding:RunOnStartup`). It compares against a literal list on purpose: a test that recomputed
the expectation from `ModuleSchemas` would agree with the bug.

## Delete behaviour — the `Activity` FK family

`Activity` is one of the two hub entities: **17 FK columns across 15 tables in 7 projects** point at it,
plus two more on the `PlannerSuggestionFrom*` view entities that carry no database constraint. Every one
is declared in its own slice's configuration — `ConfigureCrossSliceRelationships` declares none of them.

**The rule: archive is the non-destructive path, hard delete means destroy, and the cascade is the
point.** `PATCH /activity/{id}/archived` is what a user reaches for to retire an activity; `DELETE
/activity/{id}` is the explicit destructive choice, and it takes history, planner tasks, to-do items,
routines (with their `Streak` / `BestStreak`), profiles, memory anchors, presets and tracker mappings
with it. `usageCount` / `canDelete` on the activity grid — summed across the `IActivityReferenceSource`
implementations — is what stops the UI offering the delete in the first place.

**`Restrict` is not the alternative it looks like.** `activity_id` is `NOT NULL` on 10 of the 17 columns,
so the dependent row genuinely cannot outlive its activity. `Restrict` there would not protect data, it
would make any activity that was ever used permanently undeletable.

**One exception, and one that is not available.** `TodoListItem.PairedLeisureActivityId` is `SetNull` —
it is the optional other half of a temptation bundle, so losing the reward activity unpairs the task
rather than destroying it. The two `Tracker*MappingByPattern.ActivityId` columns are nullable too but
still `Cascade`, because `SetNull` is impossible there: `CK_Tracker*MappingByPattern_TargetRequired`
requires *exactly one* of ignored / activity / role-or-category, so a blanked mapping fails the check
constraint rather than surviving as an unmapped rule.

⚠ **The authoritative inventory is `ActivityForeignKeyInventoryTests`, not this section.** It freezes
every activity FK with its `IsRequired` / `IsUnique` / `DeleteBehavior`, asserted off `dbContext.Model`,
and a new activity FK fails it by existing — which is the intent, because adding one is a decision about
what a delete destroys. Read that file's remarks before changing a row; some rows are frozen without
being settled and say so. Prose in this repo has been confidently wrong about this exact subject before
(`DeletingActivity_CascadesToItsPlannerTasks_NotRestrict` exists to correct a doc claiming `Restrict`).

⚠ **Both ways this goes wrong are one word and silent.** `WithOne()` where `WithMany()` was meant puts a
**unique** index on the FK and caps the relationship at 1:1 — the entity still reads like an ordinary
nullable FK. Omitting `OnDelete` on an *optional* relationship gets EF's `ClientSetNull` default, which
leaves the database constraint at `NO ACTION`, so the delete is refused with a 409 naming no entity.
Both shipped on the tracker mappings, neither broke a build, a test or a log line, and both were found
by reading configurations. Prefer `IsManyWithOneActivity()` (default `Cascade`, always `IsRequired`);
when hand-rolling `HasOne(...)`, state `OnDelete` explicitly and pin the constraint name.

## DbContext helpers

`framework/Sydowwe.Framework/infrastructure/persistence/DbContextExtensions.cs` is the single copy.
It exposes `DbContextHelper` — Result-returning CRUD helpers that wrap `SaveChanges` with
`DbUtils.HandleException`:

- `BaseSaveChangesAsync()` — stamps `CreatedTimestamp` / `ModifiedTimestamp` for `BaseTableEntity`
  entries. Called by the `SaveChangesAsync()` override and inside the helper methods — you do not need
  to call it manually.
- `AddEntityAsync`, `AddRangeAsync` (transactional, chunks of 300)
- `UpdateEntityAsync`, `UpdateRangeAsync`
- `DeleteEntityAsync`, `DeleteRangeAsync`, `DeleteByIdAsync`
- `SetActiveStatusAsync`, `SetActiveStatusRangeAsync` — for `ISoftDeletable` (`IsActive`).

## User-scoping query filters

⚠ **A query filter must read the current-user id off the DbContext, never off a captured service.** EF
evaluates any subtree of a filter that it can evaluate and that does **not** reference the DbContext,
inlines the result into the SQL as a **literal**, and caches the compiled query — per EF internal
service provider, which every host configuring the DbContext identically shares. So a filter built
from `Expression.Constant(loggedUserService)` resolves the user *once*, when the shape is first
compiled, and every later execution — any user, any request, any host in the process — runs with the
first caller's id. Nothing throws and nothing logs; the second user reads the first user's rows.

A member read rooted at the context is the documented exception: EF rewrites it into an accessor over
the executing context and re-evaluates it per execution. Hence `IUserScopedDbContext.ScopeUserId`, a
property on `BaseDbContext` — use it, and keep new hand-written filters (`AppDbContext`'s
`WebExtensionActivityEntry` one) reading context members too. Note `EF.Parameter` does **not** work
here: EF 10 throws while normalizing the filter. Guard is `UserScopingQueryFilterTests` — it asserts
the generated SQL parameterizes the id, and that two users hitting one endpoint in one process each
see only their own rows.

## Seeding

One copy, in `framework/Sydowwe.Framework/infrastructure/persistence/seeder/`. Pick the seeder kind by
two questions: **who owns the rows**, and **is this production data or a fixture**.

|                 | App-wide (no user owner)                            | Per-user                                                       |
|-----------------|-----------------------------------------------------|----------------------------------------------------------------|
| **Production**  | `IAppWideDefaultSeeder` — `Seed(bool overrideData)` | `IPerUserDefaultSeeder` — `SetupDefaults` / `ResetDefaults`     |
| **Dev fixture** | `IAppWideDevSeeder` — `Seed()` + `TruncateTable()`  | `IPerUserDevSeeder` — `SeedForUser(userId)` + `TruncateTable()` |

Set `SeederName` + `Order` (from `IDatabaseSeeder`, which is identity only) and add a lifetime marker
— the DI scan registers it and the matching manager picks it up. No manual registration. Read
`AdhdTimeOrganizer.Core/.../seeder/SeederOrderBands.md` before adding one anywhere.

- **Only dev seeders truncate.** Default seeders upsert: `overrideData` means "update existing rows in
  place", never "wipe and re-insert". Truncating `user_role` / `user` cascades away every user↔role
  assignment. Data that wants wipe-and-reinsert is a fixture — use a `…DevSeeder`. Truncation runs in
  reverse `Order`, so express FK dependencies once via `Order`.
- **Managers** (`interface/manager/`, one per cell): `IAppWideDefaultSeederManager`,
  `IPerUserDefaultSeederManager` (`SeedAllForUserAsync` is the sign-up path, via `UserDefaultsService`),
  `IAppWideDevSeederManager`, `IPerUserDevSeederManager` (`SeedAllForRootAdminAsync`). Default managers
  let exceptions propagate; dev managers log and continue. Both dev managers also expose
  `SeedAssembly…Async` (reseed one module) and `TruncateAllTablesAsync`.
- **Don't hand-write a per-user default seeder — subclass `BasePerUserDefaultSeeder<TEntity>`.** It
  supplies `SetupDefaults` / `ResetDefaults`; you supply `Defaults(userId)`, `Collides(a, b)` — "would
  these two rows violate one of this table's unique indexes?" — and `Apply(target, default)`. Every
  per-user default seeder in the portal uses it except `CalendarSeeder`, whose key is a date range
  rather than a row set.
  - **Both operations key off `Collides`, never off row counts.** Comparing `defaults.Count` to the
    user's row count reads as a guard but isn't one: a user holding *some* defaults looks unseeded and
    gets the whole set re-inserted (23505), and a positional reset rewrites a key column onto a row
    while a sibling still holds the incoming value (23505 again). Both shipped here;
    `PerUserDefaultMatcher` + `PerUserDefaultMatcherTests` are what keep them dead.
  - **The key is rarely `Text`.** `TaskPriority` is `(user_id, priority)`, `TaskImportance` is
    `(user_id, importance)`, `ActivityRole` is `(user_id, name)`, `RoutineTimePeriod` has *two* unique
    indexes and needs both. Check the configuration before writing `Collides`.
  - **Seeder reads use `IgnoreQueryFilters()`** — mandatory, and the reason `CalendarSeeder` does it by
    hand. `UserScoping` is on in this portal, so an `IEntityWithUser` read is scoped to the *ambient*
    user; a seeder told to seed a different user would read back zero rows and re-insert everything.
    The explicit `UserId` predicate is the scoping.
  - **Reset does not call `UpdateRange`.** The rows are tracked, so `SaveChanges` writes only what
    `Apply` changed; marking them all `Modified` rewrites every column and bumps `ModifiedTimestamp`
    on rows that already match.
- **Finding users:** never query users from a seeder or manager in Framework — use `ISeedUserProvider`
  (`GetAllUserIdsAsync` / `GetRootAdminUserIdAsync` / `GetSeedUserIdsAsync`). The portal implements it
  in `infrastructure/persistence/seeder/SeedUserIdProvider.cs`, alongside Contracts'
  `ISeedUserIdProvider`.
- Entry point is `Program.SeedDatabase` — four ordered passes, **all still commented out**, so nothing
  seeds on startup today.

## Ledger retention

Append-only ledgers (`scheduled_job_run`, `reminder_dispatch`, notification history, …) need a
retention purge or they grow forever — GDPR Art. 5(1)(e) / §13 zák. 18/2018.

Bind the **policy** from
`framework/Sydowwe.Framework/infrastructure/persistence/retention/RetentionOptions.cs` (`Enabled`,
`RetentionYears`, `KeepLastN` + `CutoffUtc()` / `CutoffOffset()`): subclass it per module with a
`SectionName` and `services.Configure<>` it — see `framework/Sydowwe.Scheduler/application/job/SchedulerRetentionOptions.cs`
and `framework/Sydowwe.Reminders/application/job/ReminderRetentionOptions.cs`.

Write the **query** as plain LINQ in the module's own purge handler — there is deliberately no shared
delete helper, because the FK guards that differ per ledger are the hard part and can't be shared.
Examples to copy: `PurgeExpiredRunLogsJobHandler.cs` (one pass),
`PurgeExpiredReminderLedgersJobHandler.cs` (three ordered passes, two self-FKs),
`PurgeExpiredNotificationHistoryJobHandler.cs`. The shape is: age gate → keep-last-N floor
(`Count(newer => …) >= keepLastN`) → FK guards → one `ExecuteDeleteAsync`.

With `Restrict` FKs, delete in dependency order and exclude still-referenced rows, or the whole batch
aborts. `ExecuteDeleteAsync` bypasses the ChangeTracker (and therefore any interceptor) — correct for
`[NoAudit]` ledgers, wrong for entities you want audited.

## Auditing — available, NOT wired up

The machinery exists in `Sydowwe.Framework` — `infrastructure/persistence/audit/AuditSaveChangesInterceptor.cs`,
the `AuditLog` / `BusinessAuditLog` entities, `IAuditService` (+ `AuditService`), `[NoAudit]` /
`[AuditIgnore]` — and some module entities already carry the attributes. But the interceptor is **not**
registered on `AppDbContext` (`Program.cs` only adds `SuggestionPatternRefreshInterceptor`), the audit
entity configurations live in an assembly `AppDbContext` never applies, and there is no `audit_log`
migration. **Nothing is written today — don't tell yourself CRUD is being captured.**

Turning it on needs all three: `options.AddInterceptors(…AuditSaveChangesInterceptor…)` in the
`AddDbContext` callback, the audit entity configurations applied to the model, and a migration.
`audit_log` is partitioned by `Date` (yearly RANGE, composite PK `(Id, Date)`) — governed by
`AuditLogEntityConfiguration.cs` (`FirstYear`, `YearCount`); `business_audit_log` is not partitioned.

Opt-outs, for when you do write auditable entities: `[NoAudit]` on a class skips the entity entirely;
`[AuditIgnore]` on a property keeps the entity audited but excludes that column from snapshots and
`ChangedProperties` (use for sensitive PII fields).
