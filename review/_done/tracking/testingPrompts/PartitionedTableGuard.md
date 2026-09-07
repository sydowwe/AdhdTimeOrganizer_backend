# TEST-8 — the partition-key guard the docs say exists, and does not

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

**Status today:** `docs/testing.md:176` states that `TrackingRouteSmokeTests` covers silent failures
1, 4 **and 5**. Failure #5 is *"Tracking's `ApplyConfigurationsFromAssembly` call missing from
`AppDbContext` → the two partitioned tables quietly become plain tables in the next migration"*
(`summary.md:151-152`). The class's own XML doc (`TrackingRouteSmokeTests.cs:35-37`) even `<see cref>`s
a `PartitionedTrackingTables_KeepTheirPartitionKey` method.

**No such test method exists.** The file's five methods are the two route theories,
`DesktopDistinctProcessesRoute_IsRegistered`, `WebExtensionActivityEntry_KeepsBothOfItsQueryFilters` and
`RetentionPurgeJob_IsRegisteredAndScheduled`. The loudest-documented silent failure in the module is
unpinned, and the docs say otherwise — which is the worse of the two errors (`DOC-5`).

## What has to be true

- `AppDbContext.ApplyHostConfigurations` calls
  `ApplyConfigurationsFromAssembly(typeof(DesktopActivityEntryConfiguration).Assembly)` — the anchor is
  a type that cannot move slices (`summary.md`'s closing warning; the Planning extraction already
  turned a mis-anchored scan into ~500 lines of table renames).
- `DesktopActivityEntryConfiguration` and `WebExtensionActivityEntryConfiguration` each call
  `builder.IsPartitionedByRange("record_date", [...])` with the yearly partitions y2026–y2030 plus a
  `_default`.
- Each also overrides the base's single-column PK with a composite `(Id, RecordDate)` — Postgres
  requires the partition key in the primary key — and declares `row_version` as
  `IsConcurrencyToken(false).ValueGeneratedNever()`, because a partitioned table cannot return `xmin`
  in a `RETURNING` clause.

Drop the assembly scan and **all five** of those facts vanish at once, with no build error: the entities
still map, the tables still work, and the next `migrations add` emits a silent de-partitioning.

## Scenarios

Put these in `TrackingRouteSmokeTests.cs` under the name the XML doc already promises —
`PartitionedTrackingTables_KeepTheirPartitionKey` — so the `<see cref>` starts resolving, then fix the
second stale `<see cref>` (`WebExtensionActivityEntry_KeepsItsCombinedQueryFilter` → the actual
`…_KeepsBothOfItsQueryFilters`).

Read the model from the **host-built** context, the way the query-filter test does:

```csharp
using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
var model = scope.ServiceProvider.GetRequiredService<AppDbContext>().Model;
```

1. `PartitionedTrackingTables_KeepTheirPartitionKey`
   For both `DesktopActivityEntry` and `WebExtensionActivityEntry`, assert the annotation
   `IsPartitionedByRange` writes is present and names `record_date`. **Find the annotation name from
   `Sydowwe.Framework.infrastructure.persistence`** (the `PartitionDefinition` / `IsPartitionedByRange`
   helper) rather than guessing a string — the assertion is worthless if it reads a key nothing sets.
2. `PartitionedTrackingTables_KeepTheirCompositePrimaryKey`
   `model.FindEntityType(typeof(T))!.FindPrimaryKey()!.Properties` is exactly `[Id, RecordDate]`, in
   that order, for both. This is the cheapest and most robust half — it needs no annotation knowledge
   and it fails the moment the configuration stops being applied.
3. `PartitionedTrackingTables_KeepTheirNonConcurrencyRowVersion`
   `FindProperty("row_version")!.IsConcurrencyToken` is `false` and `ValueGenerated` is `Never` on both.
   The comment in each configuration explains why; that reasoning is invisible in a green build.
4. `TrackingEntities_ResolveToTheTrackingSchema` — **check `ModuleSchemaTests` first.** It already
   asserts every entity's schema solution-wide; if the five tracking entities are covered there, do not
   duplicate it, just note it in the XML doc.

### Optional, stronger, and more work

5. `PartitionedTrackingTables_ArePartitionedInTheDatabase` — query
   `SELECT relkind FROM pg_class WHERE relname = 'desktop_activity_entry' AND relnamespace = 'tracking'::regnamespace`
   and assert `'p'` (partitioned) rather than `'r'` (ordinary table); optionally count children in
   `pg_inherits`. This proves the fixture actually created them through the partitioned SQL generator,
   which the model-level assertions cannot.
   ⚠ Per CLAUDE.md, **hand-written SQL is not schema-qualified for you** — name the `tracking` schema
   explicitly or the query resolves against the `search_path` and silently answers about nothing.

## After the test exists

Update `docs/testing.md`'s guard table with a row for it (that file lists what each guard pins, and
this is now a real one), and remove the claim that the route smoke tests cover silent failure #5 from
the prose — the new test does.
