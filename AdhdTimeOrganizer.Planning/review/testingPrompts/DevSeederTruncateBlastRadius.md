# TEST-23 — what `TRUNCATE … CASCADE` actually takes with it

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

## The hazard (`MIG-2`)

`TaskPlannerDayTemplateSeeder.TruncateTable()` (`infrastructure/persistence/seeder/dev/`) calls
`dbContext.TruncateTableCascadeAsync<TaskPlannerDayTemplate>()`, which issues
`TRUNCATE <schema>.<table> RESTART IDENTITY CASCADE`
(`framework/Sydowwe.Framework/infrastructure/persistence/seeder/SeederExtensions.cs:76-90`).

PostgreSQL's `TRUNCATE … CASCADE` **truncates every referencing table outright**. It does *not* honour
`ON DELETE SET NULL`. And `user_planner_settings.default_apply_template_id` references
`task_planner_day_template` (`UserPlannerSettingsConfiguration.cs:29-32`, `DeleteBehavior.SetNull`).

So re-running the dev seeders should be expected to wipe **every user's planner settings**, not just the
templates — silently, and with nothing to restore them: `UserPlannerSettingsSeeder` is a *per-user
default* seeder (order 420) that does not truncate and only runs at user setup.

`CalendarSeeder.TruncateTable()` has the same shape and cascades into `planner_task` → `reminder`, which
**is** intended for a dev reset — but confirm the reach rather than assuming.

## Why a test rather than a fix

The blast radius depends on the live FK graph, which changes as slices are added. Assert it, so the next
FK into `task_planner_day_template` shows up as a failing test instead of as missing rows in someone's
dev database.

## Where

`AdhdTimeOrganizer.IntegrationTests/Seeding/` — a new `DevSeederTruncateScopeTests`. The Core review has
a sibling prompt (`DevSeederTruncateScope.md`); if that test now exists, extend it rather than starting
a second class.

## Scenario A — the finding

1. Seed, for two users: task planner day templates, template planner tasks, **and** a
   `UserPlannerSettings` row per user with `DefaultApplyTemplateId` pointing at one of the templates and
   a distinctive non-default value elsewhere (`SlotDurationMinutes = 37`, say).
2. Resolve `TaskPlannerDayTemplateSeeder` from the test host and call `TruncateTable()`.
3. Assert, against a fresh context with `IgnoreQueryFilters()`:

```csharp
(await db.Set<TaskPlannerDayTemplate>().CountAsync(ct)).Should().Be(0);
(await db.Set<TemplatePlannerTask>().CountAsync(ct)).Should().Be(0, "cascade FK from the template");

(await db.Set<UserPlannerSettings>().CountAsync(ct)).Should().Be(2,
    "TRUNCATE CASCADE must not take user_planner_settings with it — the FK is ON DELETE SET NULL");
```

The third assertion is **expected to fail** today (count 0). Write it red-and-tagged, or green against
current behaviour with a `// FINDING: MIG-2` comment naming the assertion to flip.

Follow it with the assertion that says what *should* happen instead:

```csharp
var settings = await db.Set<UserPlannerSettings>().IgnoreQueryFilters().ToListAsync(ct);
settings.Should().OnlyContain(s => s.DefaultApplyTemplateId == null, "SetNull, not deleted");
settings.Should().OnlyContain(s => s.SlotDurationMinutes == 37, "the user's other settings are untouched");
```

## Scenario B — `CalendarSeeder`'s reach (the intended one)

Seed calendars, planner tasks and reminders (portal row **and** module `ReminderDefinition` via
`ReminderSeedHelper`), then call `CalendarSeeder.TruncateTable()`.

Assert `calendar`, `planner_task` and portal `reminder` are all empty — and that the module-side
`ReminderDefinition` **survives**, because the FK cascade never reaches the module. That is the same
asymmetry every planner-task delete path is written around (`domain-map.md` § *Reminders*), and a dev
reset leaves the module holding orphaned definitions. Worth knowing and documenting even if the answer
is "acceptable in dev".

## Scenario C — schema qualification

`SeederExtensions` schema-qualifies the table name from the model, and its own doc explains why: an
unqualified name resolves against the connection's `search_path` and can truncate the wrong table when a
name exists in two schemas. Planning's tables live in the `planning` schema
(`ModuleSchemas.cs`, assembly-keyed).

Seed a row in another slice's table with a colliding name if one exists; otherwise assert the generated
SQL contains `planning.` — capture it via the test host's SQL logger, or assert indirectly that a
same-named table in another schema is untouched. `ModuleSchemaTests` already pins that this assembly
resolves to `planning`; this closes the loop from the model to the emitted statement.

## The fix these tests drive

Either null the FK before truncating —

```sql
UPDATE planning.user_planner_settings SET default_apply_template_id = NULL;
TRUNCATE planning.task_planner_day_template RESTART IDENTITY;   -- no CASCADE
```

— or accept the cascade and re-run `UserPlannerSettingsSeeder` afterwards. The first preserves the
user's other settings and is the better default.
