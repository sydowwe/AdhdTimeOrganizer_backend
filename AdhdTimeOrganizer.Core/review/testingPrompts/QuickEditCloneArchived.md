# TEST-7 — quick-edit and clone 404 on an archived activity

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first. Finding: `CQ-3` 🟠.

**Status today:** `ActivityEndpointTests.cs:946,1003,1021,1036,1061` covers clone and quick-edit on
**active** activities plus their cross-user 404s. `ActivityArchivingTests` covers archived behaviour on
every *other* activity route. The intersection is untested.

## What the code does

`QuickEditActivityEndpoint.cs:31` and `CloneActivityEndpoint.cs:30` load with
`dbContext.Set<Activity>().FindAsync([Route<long>("id")], ct)`. `FindAsync` cannot lift a named query
filter — which is exactly why `SetArchivedActivityEndpoint.cs:57-61` uses a keyed `FirstOrDefault`
instead and says so in a comment. So:

| Route | Reaches an archived activity? |
|---|---|
| `GET api/activity/{id}` | ✅ `GetByIdActivityEndpoint.cs:24` |
| `PUT api/activity/{id}` | ✅ `UpdateActivityEndpoint.cs:19` |
| `DELETE api/activity/{id}` | ✅ `DeleteActivityEndpoint.cs:16` |
| `PATCH api/activity/{id}/archived` | ✅ `SetArchivedActivityEndpoint.cs:63-65` |
| `PUT api/activity/{id}/quick-edit` | ❌ 404 |
| `POST api/activity/{id}/clone` | ❌ 404 |

Every other row action in the archive view works; those two 404. `UpdateActivityEndpoint`'s own comment
argues that lifting the filter is safe *because the request cannot carry `IsArchived`* — and
`QuickEditActivityRequest` (`Core/application/dto/request/activity/QuickEditActivityRequest.cs`) cannot
either, so the argument applies verbatim.

## Payloads

- `PUT api/activity/{id}/quick-edit` → `{ "name": "...", "text": null, "categoryId": null }`, 204
- `POST api/activity/{id}/clone` → same body, returns `200` with the new id as a bare `long`

Both are validated by `QuickEditActivityValidator` (name required, ≤ 50; text ≤ 500; categoryId > 0).

## Add to `Endpoints/ActivityArchivingTests.cs`

This belongs in the archiving file, not the CRUD one — it is the "which endpoints see archived rows"
question that file exists to answer, and `SeedActivityAsync(db, name, isArchived: true)` is already
there.

### 1. Quick-edit reaches an archived activity — `CQ-3`, **KnownGap**

```csharp
[Fact(DisplayName = "CQ-3: quick-edit reaches an archived activity, like update does")]
[Trait("Status", "KnownGap")]
```

Seed archived; `PUT api/activity/{id}/quick-edit` with a new name. Correct: **204**, the name changed
in the database, and `IsArchived` still `true` (assert with `.IgnoreQueryFilters()`). Today: 404.

The `IsArchived` half of the assertion is the important one — it is the same guarantee
`Update_DoesNotUnarchive` (`:151`) pins for `PUT /activity/{id}`, and the fix must not introduce the
trap on the quick-edit path.

### 2. Clone reaches an archived activity — `CQ-3`, **KnownGap**

Seed archived; `POST api/activity/{id}/clone`. Correct: **200** with a new id. Then decide and assert
the product question the current 404 has been hiding: **is the clone archived?**

Recommend asserting the clone is **active** — `Activity.cs:29-33` says "a newly created activity is
always active", and cloning an archived activity is something you do in order to start using it again.
Today `Clone()` is a `MemberwiseClone` that resets `Id` only (`Activity.cs:56-61`), so it would copy
`IsArchived = true`. Write the assertion for the correct answer and let it fail; pair the fix with
`TEST-8`, which is about the same method.

### 3. Cross-user is still 404 after the fix — passes today, keep it green

No `KnownGap`. `Clone_ForeignActivityId_Returns404` and `QuickEdit_ForeignActivityId_Returns404`
already exist at `ActivityEndpointTests.cs:1021,1061`. Add an **archived + foreign** case here: the
per-user filter must stay on when the archival filter is lifted. Confusing the two is the most likely
way to break this while fixing it.

### 4. An active activity is unchanged — passes today

Regression pin so lifting the filter changes nothing on the ordinary path.

## The fix, for reference

Replace `FindAsync` in both endpoints with

```csharp
var id = Route<long>("id");
var entity = await dbContext.Set<Activity>()
    .IgnoreQueryFilters([AppQueryFilters.ActivityArchived])
    .FirstOrDefaultAsync(a => a.Id == id, ct);
```

(`using AdhdTimeOrganizer.infrastructure.persistence;` — the **host** namespace, deliberately; see
`summary.md` → Gotchas.) While in these two files, add the `AllowedRoles()` / `Roles(...)` pair for
symmetry with `MergeActivityEndpoint:60,65` — see [`CoreEndpointAuthMatrix.md`](CoreEndpointAuthMatrix.md).

## Doc reconciliation

`../../docs/domain-map.md` → Navigation index rows for `QuickEditActivityEndpoint` and
`CloneActivityEndpoint` say nothing about archival reach, while the four rows around them do.
