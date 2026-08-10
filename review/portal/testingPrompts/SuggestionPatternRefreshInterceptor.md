# TEST-14 — Integration tests for `SuggestionPatternRefreshInterceptor`

## Context you need (do not rediscover)

**Repo:** `C:/Users/jakub/RiderProjects/AdhdTimeOrganizer`
**Under test:** `AdhdTimeOrganizer/infrastructure/persistence/interceptors/SuggestionPatternRefreshInterceptor.cs`
— a `SaveChangesInterceptor` registered on `AppDbContext` in `Program.cs` (it is the **only**
interceptor registered; the audit interceptor is not wired).

**What it does:** in `SavingChangesAsync` it sets three flags depending on whether the change set
touches `PlannerTask`, `ActivityHistory` or `Calendar`. In `SavedChangesAsync` — i.e. **after
commit** — it issues `REFRESH MATERIALIZED VIEW CONCURRENTLY` for each flagged view, then clears the
flags.

**The three views:** `mv_planner_task_pattern`, `mv_activity_history_pattern`,
`mv_template_suggestion_pattern`. They are hand-written SQL in
`AdhdTimeOrganizer/infrastructure/persistence/sqlScripts/*.sql`, **not** migration output, so
`EnsureCreated` does not produce them. Two separate mechanisms install them:
- runtime: `infrastructure/persistence/SuggestionPatternViewInstaller.cs` (embedded resources +
  `to_regclass`, called from `Program.cs` just before `SeedDatabase`);
- tests: `AdhdTimeOrganizer.IntegrationTests/Infrastructure/AppDbContextFixture.cs`
  `OnSchemaCreatedAsync`, reading the files copied next to the test binaries by a `Content` item.

**Test project:** `AdhdTimeOrganizer.IntegrationTests`, new file
`Infrastructure/SuggestionPatternRefreshTests.cs`. xunit v3, FluentAssertions,
`[Collection("Postgres")]`, `CreateDbContext()`, `SeedAsync(db)` override.

## Scenarios to write

### A. `CQ-9` — a refresh failure must not turn a committed save into a 500 (should FAIL today)

The refresh runs post-commit with **no try/catch**, so any failure propagates out of
`SavedChangesAsync`. The caller sees an exception (a 500 through an endpoint) even though the data
was persisted successfully — a false negative that misleads clients and error monitoring, and a
retry then hits a duplicate-save path.

1. Drop one of the three views (`DROP MATERIALIZED VIEW mv_planner_task_pattern`) inside the test, so
   the refresh raises Postgres `42P01`.
2. Save a `PlannerTask` through `AppDbContext`.
3. Assert **both**: the save did **not** throw, **and** the `PlannerTask` row exists when read back
   from a fresh context.
4. Recreate the view in cleanup so later tests in the collection are unaffected — or use a
   dedicated fixture/DB so the drop can't leak. Note `[Collection("Postgres")]` shares a container:
   be careful here, and prefer restoring in a `finally`.

Also cover the same shape through HTTP: hit a planner-task create endpoint with a view dropped and
assert a 2xx rather than a 500.

### B. `CQ-10` — flags must not survive a failed save (should FAIL today)

The three flags are set in `SavingChangesAsync` and cleared only at the end of `SavedChangesAsync`.
There is no `SaveChangesFailedAsync` override, so a save that throws in between leaves them `true`,
and the **next** save on that same scoped context — even one touching none of the three entity types
— triggers a spurious full view refresh.

1. On one `AppDbContext` instance, attempt a `PlannerTask` save engineered to fail at the DB (e.g.
   violate a FK or a check constraint).
2. Catch the failure.
3. On the **same** context, save an entity of an unrelated type that should trigger **no** refresh.
4. Assert no refresh occurred.

To observe "did a refresh occur", the cleanest probe is timing-independent: check
`pg_stat_user_tables` / the matview's `last_refresh`-equivalent, or wrap the interceptor's SQL
execution behind a seam you can count. If neither is practical, assert indirectly by dropping a view
that should *not* be refreshed in step 3 — if the flag leaked, the save throws (today) or logs
(after `CQ-9` is fixed).

### C. Flag correctness — only the touched views refresh

One test per entity type: save a `PlannerTask`, an `ActivityHistory`, a `Calendar`, and an unrelated
entity, asserting that exactly the expected subset of views is refreshed each time. This is the
cheapest regression net for the copy-pasted `if` blocks (`CQ-34` notes the three refresh blocks are
duplicated with hardcoded view-name literals — easy to typo when a fourth view is added).

### D. `PERF-1` / `PERF-2` — characterize, don't gate

Do **not** write a wall-clock assertion; it will flake. Instead write a test that documents the
shape, so the eventual fix has a baseline:

1. Seed a meaningful number of `ActivityHistory` rows (enough that the view has real content).
2. Time a single one-row `PlannerTask` update.
3. Record the number in the test output / a comment, and assert only something very loose (e.g.
   completes within 30s) so it cannot flake but will scream if the refresh becomes pathological.

Optionally, a concurrency probe for `PERF-2`: issue N concurrent saves that each touch `PlannerTask`
and assert they all succeed. `REFRESH CONCURRENTLY` serializes rather than queueing unboundedly, so
the value here is proving no deadlock/timeout, not measuring throughput.

### E. `REFRESH CONCURRENTLY` requires a unique index

`REFRESH MATERIALIZED VIEW CONCURRENTLY` fails outright if the view has no unique index. Assert each
of the three views has one — a schema-level test reading `pg_indexes`. If any lacks it, that is a
finding, not a test to weaken: the interceptor would fail on **every** save touching that entity.

### F. `MIG-4` — the two installation paths must agree

The fixture and `SuggestionPatternViewInstaller` independently install the same three scripts by
different mechanisms (file copy vs embedded resource). A new script added to only one, or a csproj
item-type change, makes tests and runtime disagree — and the failure surfaces as a 42P01 at save
time, not as a build error.

Write a test that enumerates the embedded resources under
`infrastructure.persistence.sqlScripts` in the `AdhdTimeOrganizer` assembly and asserts the set
matches the `.sql` files the fixture copies. Cheap, and it pins a real drift risk.

## Conventions

- AAA; restore any dropped view in a `finally` — the Postgres container is shared across the
  collection.
- Scenarios **A** and **B** are expected to fail against current `main`. Tag
  `[Trait("Status","KnownGap")]` referencing `CQ-9` / `CQ-10`; remove when fixed.
- Log view names only — no entity data.
