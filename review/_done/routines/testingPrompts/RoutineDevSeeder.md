# TEST-13 — `RoutineTodoListSeeder` (dev fixture)

Read `_SHARED-CONTEXT.md` first. `Seeding/PerUserDefaultSeederTests.cs` and
`Seeding/PerUserDefaultMatcherTests.cs` are the existing models — the *other* routine seeder
(`RoutineTimePeriodSeeder`) is already covered there and in
`Endpoints/RoutineTimePeriodUniqueIndexTests.Seeder_SetupDefaults_SeedsAllFourDefaultsWithoutUniqueViolation`.
This prompt is only about the dev one, which has zero coverage.

## Subject

`infrastructure/persistence/seeder/dev/RoutineTodoListSeeder.cs` — `IPerUserDevSeeder`, `Order = 200`
(Routines owns band 200–299; the contract is
`AdhdTimeOrganizer.Core/infrastructure/persistence/seeder/SeederOrderBands.md`, read it first).

Two members:

- `TruncateTable()` → `dbContext.TruncateTableCascadeAsync<RoutineTodoList>()`
- `SeedForUser(long userId)` → builds up to nine `RoutineTodoList` rows by matching activities **by
  name** ("Morning Exercise", "Meditation", …) against periods **by text** ("Daily", "Weekly",
  "Monthly").

## Why it is worth testing

1. **It truncates every user's routine items** despite being a per-user seeder. That is the documented
   contract ("only dev seeders truncate") and it is *correct* — but it is also the single most
   destructive line in the module, and nothing pins that it stays confined to dev seeding. It also
   bypasses the ChangeTracker entirely (`AUDIT-2`).
2. **Every lookup is a silent no-op on miss.** `FirstOrDefault(a => a.Name == "Morning Exercise")`
   returns null and the corresponding `if` block is simply skipped (`:91-113` and the eight like it).
   Rename an activity in the activity seeder and this one quietly seeds fewer rows — no error, no log
   line beyond the final count. Same for the three period lookups by `Text` (`:54-56`).
3. `MinAsync(rtl => (int?)rtl.DisplayOrder) ?? 0` with a `TimePeriodId == (period != null ? period.Id : 0)`
   guard (`:72-85`) is doing null-handling inside a LINQ expression three times over. It works, but it
   is the kind of thing that breaks silently under an EF upgrade.

## What to write

New file `Seeding/RoutineDevSeederTests.cs`.

- `SeedForUser_WithTheStandardActivityFixture_SeedsTheExpectedRows` — run the activity + period seeders
  first (see how `PerUserDefaultSeederTests` composes seeders), then this one, and assert the **count
  and shape**: nine rows, correctly distributed across the Daily/Weekly/Monthly periods, steps attached
  where the seeder declares them. This is the test that fails loudly when an activity gets renamed —
  which is the whole point.
- `SeedForUser_WithNoActivities_SkipsCleanlyAndLogsAWarning` — `:41-45`. Assert no rows and no throw.
- `SeedForUser_WithNoTimePeriods_SkipsCleanly` — `:47-51`.
- `SeedForUser_AssignsDescendingDisplayOrderWithinEachPeriod` — the three `nextOrder*` counters
  decrement by `DisplayOrderGap` independently per period (`:112`, `:130`, …). Assert the orders within
  one period are distinct and descending, and that two periods' orders do not depend on each other.
- `SeedForUser_TwiceWithoutTruncate_ViolatesTheUniqueIndex` — `(UserId, TimePeriodId, ActivityId)` is
  unique, so re-seeding without truncating must fail rather than silently duplicating. Pins that
  truncate-then-seed is the required order, which is currently only a comment (`:29`).
- `TruncateTable_RemovesEveryUsersRoutineItems` — seed for two users, truncate, assert **both** are
  gone. State in the test's `<summary>` that this is the intended dev-seeder contract, not a bug, so
  nobody later "fixes" it into a per-user delete. Also assert it does **not** touch
  `RoutineTimePeriod` or `RoutinePeriodCompletion` — `TruncateTableCascadeAsync<RoutineTodoList>()`
  cascades, and what exactly it cascades to is worth pinning.

## Schema warning

`TruncateTableCascadeAsync` issues hand-written SQL, and per CLAUDE.md hand-written SQL is **not**
schema-qualified for you — every module's tables live in their own schema (`ModuleSchemas.cs`), so an
unqualified `TRUNCATE` resolves against the `search_path`. The helper lives in the framework
submodule; if this test surfaces a schema-resolution problem, the fix is a **two-repo operation**
(commit and push `framework/` first, then the gitlink here). Check `Infrastructure/ModuleSchemaTests.cs`
— it may already guard this.
