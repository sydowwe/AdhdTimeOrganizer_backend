# TEST-1 — deleting an activity role or category destroys every activity under it

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first. Finding: `SEC-1` 🔴.

> ## ✅ Done, 2026-09-01 — this prompt has been executed and `SEC-1` fixed
>
> `AdhdTimeOrganizer.IntegrationTests/Endpoints/ActivityLookupDeleteCascadeTests.cs` exists and is
> green, with **no `KnownGap` traits** — cases 1–5 below were written against the corrected behaviour
> and the behaviour was corrected in the same commit, so they assert rather than pin. Two cases were
> added beyond this prompt, both about the archival filter: an archived activity survives its
> category's delete, and an archived activity still blocks its role's. Case 6 landed as
> `ExpectedOutbound` / `EveryForeignKeyOutOfActivity_MatchesTheFrozenInventory` in
> `Modules/ActivityForeignKeyInventoryTests.cs`, with `CategoryId` frozen at `SetNull` rather than
> `Cascade` — the literal below describes the pre-fix model. Everything else here is left as written.

**Status when written:** `ActivityEndpointTests.cs:250` (`ActivityRoleDeleteTests`) and `:326`
(`ActivityCategoryDeleteTests`) are `BaseDeleteEndpointTests` subclasses that delete an **unused**
lookup row and assert 204 + a cross-user 404. `ActivityCategoryBatchDeleteTests:357` does the same in
bulk. Nothing anywhere asserts what a delete takes with it. This is the largest gap in Core and it is
invisible because a green delete test sits right next to it.

## What the code does

`AdhdTimeOrganizer.Core/infrastructure/persistence/configuration/activity/ActivityConfiguration.cs:25-34`

```csharp
builder.HasOne(e => e.Role).WithMany(r => r.Activities)
    .HasForeignKey(e => e.RoleId).OnDelete(DeleteBehavior.Cascade).IsRequired();

builder.HasOne(e => e.Category).WithMany(r => r.Activities)
    .HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.Cascade);   // optional FK, cascading
```

So `DELETE api/activity-category/{id}` returns **204** and Postgres deletes every `Activity` with that
`category_id` — and each of those cascades again through all 17 real activity FKs (history rows,
planner tasks, to-do items, profiles, presets, tracker mappings). There is no `usageCount`, no
`canDelete`, no 409 and no log line. The whole reference-counting apparatus Core built for `Activity`
does not exist one level up.

## Endpoints

- `DELETE api/activity-category/{id}` — `Core/application/endpoint/activity/category/command/DeleteActivityCategoryEndpoint.cs:7`
- `POST api/activity-category/batch-delete`, body `{ "ids": [1,2] }` —
  `…/category/command/BatchDeleteActivityCategoryEndpoint.cs:7`
- `DELETE api/activity-role/{id}` — `…/role/command/DeleteActivityRoleEndpoint.cs:15` (already answers
  409 for a `SystemKey` role; that path is covered by `ActivityRoleSystemKeyTests:166`)

## Create `Endpoints/ActivityLookupDeleteCascadeTests.cs`

Use `[Collection("Postgres")]` + `PostgresTestBase`. Seed with the helpers in the shared context. For
the cross-slice assertions seed one `ActivityHistory` row against the activity — copy the shape from
`ActivityArchivingTests.cs:316` (`HistoryRows_StillResolveAnArchivedActivity`), which already builds
one.

### 1. Deleting a category must not destroy the activities in it — `SEC-1`, **KnownGap**

```csharp
[Fact(DisplayName = "SEC-1: deleting a category blanks the classification, it does not destroy the activity")]
[Trait("Status", "KnownGap")]
```

Seed: a category, an activity in it (with a role of its own), and one `ActivityHistory` row against
that activity. `DELETE api/activity-category/{categoryId}` → 204.

Then, in a fresh `CreateDbContext()` with `.IgnoreQueryFilters()`:

- the `Activity` row **still exists**, and its `CategoryId` is now `null`
- the `ActivityHistory` row **still exists**

Today: both rows are gone. Assert with `because` strings that name the intent — losing a category is
losing a classification, not losing the thing classified.

### 2. Batch-deleting categories has the same blast radius — `SEC-1`, **KnownGap**

Same setup with two categories and two activities. `POST api/activity-category/batch-delete` with both
ids. Assert both activities survive with null `CategoryId`. This case matters on its own because it is
the one a user reaches from a multi-select in Settings, where the count of affected rows is largest
and the confirmation dialog says the least.

### 3. Deleting a role that has activities is refused — `SEC-1`, **KnownGap**

```csharp
[Fact(DisplayName = "SEC-1: deleting a role that still has activities is refused, not cascaded")]
[Trait("Status", "KnownGap")]
```

Seed a role, two activities under it, one history row. `DELETE api/activity-role/{roleId}`.

Correct behaviour: **409** (the endpoint already has a 409 path for `SystemKey`, so the client needs no
new branch), with both activities and the history row intact. `RoleId` is a required FK so `SetNull` is
not available — refusing is the only non-destructive answer.

Today: 204, and all four rows are gone.

### 4. Deleting an **unused** role or category still succeeds — passes today, keep it green

No `KnownGap` trait. This is the regression pin that stops the fix for #1–#3 from over-correcting into
"lookups can never be deleted". Seed a role with no activities and a category with no activities;
both deletes → 204; both rows gone.

### 5. A lookup's usage count is reported — `SEC-1`, **KnownGap**

```csharp
[Fact(DisplayName = "SEC-1: role and category reads carry usageCount/canDelete like activities do")]
[Trait("Status", "KnownGap")]
```

`GET api/activity-role/{id}` and `GET api/activity-category/{id}` on a lookup with two activities under
it should carry `usageCount: 2` / `canDelete: false`, matching the contract `ActivityResponse` already
publishes (`Core/application/dto/response/activity/ActivityResponse.cs:29-54`). Today neither response
type has the fields at all, so this test fails to deserialize them — assert on the raw JSON
(`JsonElement.TryGetProperty`) so the failure message is "the field is absent" rather than a
deserialization error.

### 6. Model-level: freeze the FKs **out of** `Activity` — `SEC-1`

Not in this file. Add a second frozen inventory to
`Modules/ActivityForeignKeyInventoryTests.cs`, in the same hand-written-literal style as the existing
one (which freezes FKs whose *principal* is `Activity` and therefore structurally cannot see these
two):

```csharp
// FKs whose DEPENDENT is Activity.
new("Activity", "RoleId",     true,  false, DeleteBehavior.Cascade),
new("Activity", "CategoryId", false, false, DeleteBehavior.Cascade),   // ← the one under review
new("Activity", "UserId",     true,  false, DeleteBehavior.Cascade),
```

Keep it green against today's model and let it fail loudly when `CategoryId` moves to `SetNull` — the
test failing *is* the reminder to update the literal, exactly as the existing inventory's docstring
describes.

## Doc reconciliation for whoever fixes this

`../../docs/domain-map.md` → Invariants #3 and a new Business rule "Deleting a role or a category";
`../../docs/summary.md` → Gotchas (where the `usageCount` promise stops). See `DOC-1` and `DOC-7`.
