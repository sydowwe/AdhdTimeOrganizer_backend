# TEST-14 — the dev seeders truncate app-wide while their comments say per-user

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first. Finding: `CQ-11` 🟡.

**Status today:** Core's three per-user **dev** seeders — `ActivityRoleSeeder` (Order 20),
`ActivityCategorySeeder` (30), `ActivitySeeder` (40) — are executed by **no test**.
`PerUserDefaultSeederTests` and `PerUserDefaultMatcherTests` cover the per-user *default* seeders
thoroughly (14 + 9 tests); the dev seeders have nothing.

## What the code does

`infrastructure/persistence/seeder/dev/ActivitySeeder.cs:16-19`

```csharp
public async Task TruncateTable()
{
    await dbContext.TruncateTableCascadeAsync<Activity>();
}
```

and then, at `:23`:

```csharp
// Note: TruncateTable already removed existing data for this user
```

`TRUNCATE … RESTART IDENTITY CASCADE` empties the table for **every** user. Same shape in
`ActivityRoleSeeder.cs:17-20` and `ActivityCategorySeeder.cs:17-20`. The behaviour is correct for the
framework's dev-reset flow (which truncates in descending `Order` before re-seeding in ascending
order); the **comment** is wrong, and the comment is what a future reader will act on.

Two things that are already right and should be pinned so they stay right:

- The truncate **is** schema-qualified from the EF model
  (`framework/Sydowwe.Framework/infrastructure/persistence/seeder/SeederExtensions.cs:79-96`), which
  matters because Core's tables span three schemas (`user`, `activity`, `public`) and CLAUDE.md's
  hand-written-SQL rule would otherwise apply.
- The `Order` values encode a real FK chain — ActivityProfiles lookups 10–13 → role 20 → category 30 →
  activity 40 → profiles 50–52 → anchors 60 — shared across two projects, documented in
  `infrastructure/persistence/seeder/SeederOrderBands.md`. Truncation reverses it and therefore deletes
  dependents first.

## Create `Seeding/DevSeederTests.cs`

⚠ **These tests truncate shared tables.** Every other test in the `"Postgres"` collection shares the
container. Either:

- give this class its **own** collection and fixture instance (preferred — check how
  `AppDbContextFixture` is registered and whether a second collection is viable), or
- if that is not available, assert on the seeder objects' *contract* rather than executing them: their
  `Order`, `SeederName`, and that `TruncateTable` resolves to a schema-qualified statement. That is a
  weaker test and still worth having.

Do **not** add truncating tests to the shared `"Postgres"` collection. A test that empties
`activity.activity` mid-run will fail a dozen unrelated tests in a way that looks like flakiness.

### 1. `TruncateTable` empties the table for every user, not just one

```csharp
[Fact(DisplayName = "CQ-11: the dev seeder's truncate is app-wide, not per-user")]
```

Seed activities for two users. Call `ActivitySeeder.TruncateTable()`. Assert **both** users' rows are
gone (`.IgnoreQueryFilters()`).

This test does not change any behaviour — it makes the actual scope executable, so the misleading
comment cannot survive a reader who checks. Reference the comment's file and line in the `because`
string.

### 2. Truncation is schema-qualified

```csharp
[Fact(DisplayName = "CQ-11: the dev truncate names the schema, so it cannot hit search_path")]
```

Core's tables are split across `user`, `activity` and `public`. Seed an activity, truncate, assert the
row is gone from `activity.activity` specifically — a raw
`SELECT count(*) FROM activity.activity` through `Database.SqlQueryRaw<long>` is the assertion that
actually proves the schema, since an EF read would resolve through the model either way.

If a second collection is not available, this is the case to keep in the weaker form: assert
`ModuleSchemas` maps `Activity` to `activity` and leave the execution to `ModuleSchemaTests`, which
already covers the mapping.

### 3. The seeders run in FK order

```csharp
[Fact(DisplayName = "CQ-11: dev seeder Orders match the documented FK chain")]
```

Model-level, safe in any collection — no execution. Resolve `IEnumerable<IPerUserDevSeeder>` from the
DI container and assert Core's three sit at 20 / 30 / 40 and inside the 010–099 band
`SeederOrderBands.md` assigns them.

`CoreRouteSmokeTests.CoreSeeders_AreRegisteredExactlyOnce` already pins that they are registered once;
this pins that they are *ordered* correctly, which is the half that fails as a FK violation during a
dev reset rather than as a duplicate.

### 4. `ActivitySeeder` skips gracefully when roles are missing

```csharp
[Fact(DisplayName = "CQ-11: ActivitySeeder logs and returns when the user has no roles")]
```

`ActivitySeeder.cs:34-38` returns early with a warning when `roles` is empty — a real branch, since
`ActivityRoleSeeder` running out of order would produce exactly that state. Reuse the in-memory
`ILogger` capture from `Seeding/DefaultUsersSeederTests.cs:378-420` (the only log-assertion harness in
the suite) and assert the warning is logged and no `Activity` row is written.

## The fix, for reference

Correct the comment in all three files — the table is emptied for **every** user; only the re-seed is
per-user. One sentence each, no behaviour change.

## Doc reconciliation

None. `infrastructure/persistence/seeder/SeederOrderBands.md` is accurate and is the right reference
for the ordering test.
