# TEST-9 — grid text filters are case-sensitive

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first. Finding: `PERF-2` 🟡 (a UX defect first, an
index problem second).

**Status today:** `ActivityArchivingTests:175,189,207` exercise the activity grid's `isArchived`
filter. `BaseFetchTableEndpointTests` exercises the category grid's role column and sorting. **No test
posts a text filter to any of the three grids**, so none of the seven `Contains` predicates has ever
been asserted.

## What the code does

`GridActivityEndpoint.cs:90-103`, `GridActivityRoleEndpoint.cs:15-25`,
`GridActivityCategoryEndpoint.cs:29-33` all filter with `.Contains(...)`:

```csharp
if (!string.IsNullOrWhiteSpace(filter.Name))
    query = query.Where(a => a.Name.Contains(filter.Name));
```

Npgsql renders `string.Contains` as `LIKE '%x%'`, which in Postgres is **case-sensitive** with the
default collation. So a user with an activity called `Deep Work` types `deep` into the Name filter and
gets an empty grid. The row exists, the filter is "working", and there is no error — the single most
plausible way for a user to conclude the search is broken.

Seven predicates are affected: activity `Name`, `Text`, `RoleName`, `CategoryName`; role `Name`,
`Text`, `Icon`; category `Name`, `Text`. (Role/category `Color` uses `==`, which is correct for a hex
code.)

## Route and payload

`POST api/activity/filtered-table`:

```json
{ "page": 1, "itemsPerPage": 100, "sortBy": [],
  "useFilter": true, "filter": { "name": "deep" } }
```

`ActivityArchivingTests.cs:59-71` (`FetchTableAsync`) has a ready-made helper and response record
(`GridPage` / `ActivityRow`) — copy them.

⚠ **`useFilter: true` matters.** `BaseGridEndpoint` skips `ApplyCustomFiltering` entirely when it is
false, and `GridActivityEndpoint.ApplyBaseFiltering:42-43` then means "active only". Sending a filter
object with `useFilter: false` silently applies nothing.

## Create `Endpoints/GridTextFilterTests.cs`

### 1. Filtering is case-insensitive — `PERF-2`, **KnownGap**

```csharp
[Theory(DisplayName = "PERF-2: grid name filters match regardless of case")]
[Trait("Status", "KnownGap")]
[InlineData("deep")]
[InlineData("DEEP")]
[InlineData("DeEp")]
```

Seed an activity named `Deep Work {guid}`. Filter on each casing; each must return the row.

Today only an exactly-cased substring matches.

### 2. Exact-case matching still works — passes today, keep it green

`"Deep"` returns the row. The regression pin for the `ILike` change.

### 3. A non-matching filter returns nothing — passes today, keep it green

`"Shallow"` returns zero rows. This is the pin that matters: the natural way to over-fix case
sensitivity is to loosen the predicate too far (e.g. splitting on whitespace and OR-ing terms), and
this catches it.

### 4. The role and category grids behave the same — `PERF-2`, **KnownGap**

`POST api/activity-role/filtered-table` and `api/activity-category/filtered-table` with a lower-cased
substring of a seeded name. Three grids, one rule, and a fix applied to the busiest one only is the
likely half-fix.

### 5. `%` and `_` in the filter are treated literally — passes today, keep it green

Seed an activity named `100% Focus {guid}`; filter on `100%`. EF parameterises and escapes LIKE
metacharacters for `Contains`, so this passes today — assert it so that a hand-rolled
`EF.Functions.Like($"%{v}%")` fix (which does **not** escape) cannot land without failing. Also seed a
second activity named `1005 Focus {guid}` and assert it is **not** returned, so an unescaped `%`
wildcard is visible as a wrong row rather than as a merely-too-large result.

## The fix, for reference

`EF.Functions.ILike(a.Name, $"%{filter.Name}%")` — Npgsql-specific, translates to Postgres `ILIKE`, and
keeps parameterisation. Do **not** use `.ToLower().Contains(...)`: it produces `lower(name) LIKE …`,
which is unindexable in a different and worse way and changes behaviour under non-invariant collations.

If free-text grid filters stay, a `pg_trgm` GIN index on `activity.name` becomes worthwhile — but only
after the `ILike` change lands, and it is a separate migration (`MIG-6` in
[`../03-risks-rollout.md`](../03-risks-rollout.md)). Below a few thousand activities per user the
sequential scan is cheaper than maintaining the index.

## Doc reconciliation

None.
