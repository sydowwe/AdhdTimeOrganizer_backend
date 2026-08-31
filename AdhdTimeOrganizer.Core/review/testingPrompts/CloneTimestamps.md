# TEST-8 — `Activity.Clone()` copies the source's timestamps

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first. Finding: `CQ-4` 🟡.

**Status today:** `ActivityEndpointTests.cs:946` (`Clone_CreatesNewRowAndLeavesSourceAndItsChildrenUntouched`)
and `:1003` (`Clone_WhenRequestNameMatchesSource_AppendsCopySuffix`) assert on the new row's name and
on the source being untouched. Neither looks at the audit columns.

## What the code does

`Core/domain/model/entity/activity/Activity.cs:56-61`

```csharp
public Activity Clone()
{
    var cloned = (Activity)MemberwiseClone();
    cloned.Id = 0; // Reset ID for new entity
    return cloned;
}
```

`MemberwiseClone` copies every field. `Id` is reset; `CreatedTimestamp` and `ModifiedTimestamp` are
not. Both are initialised to `DateTime.UtcNow` in C#
(`framework/Sydowwe.Framework/domain/entity/base/BaseTableEntity.cs:6-7`) **and** configured
`HasDefaultValueSql("now()")`, so EF always sends the value the object holds — meaning the clone is
persisted claiming it was created whenever the original was.

Also copied: `IsArchived` (see [`QuickEditCloneArchived.md`](QuickEditCloneArchived.md) case 2, which
is the same method and should be fixed in the same pass) and `UserId` (correct — the clone belongs to
the same user). `row_version` is a **shadow** property (`Property<uint>("row_version")` in
`EntityBuilderExtensions.cs:22-24`), so it is not a field on the object and is not copied; the
concurrency token is fine.

## Add to `Endpoints/ActivityEndpointTests.cs` — the `ActivityAreaBehaviorTests` class

That class (`:797`) is where the existing clone tests live. Keep them together.

### 1. The clone is created now, not when the source was — `CQ-4`, **KnownGap**

```csharp
[Fact(DisplayName = "CQ-4: a cloned activity carries its own creation timestamp")]
[Trait("Status", "KnownGap")]
```

Seed an activity with an explicitly **old** timestamp so the assertion cannot pass by accident:

```csharp
var activity = new Activity { UserId = UserId, Name = $"Clone Source {Guid.NewGuid():N}", RoleId = roleId };
db.Set<Activity>().Add(activity);
await db.SaveChangesAsync(CancellationToken);

activity.CreatedTimestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
activity.ModifiedTimestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
await db.SaveChangesAsync(CancellationToken);
```

`POST api/activity/{id}/clone` with `{ Name = "Something Else" }` → 200, returns the new id.

Assert on the **clone's** row in a fresh context:
- `CreatedTimestamp` is within a minute of `DateTime.UtcNow`
- `ModifiedTimestamp` likewise
- and, so the test cannot pass because the seed did not take, assert the **source** still reads 2020

Today the clone reads 2020 on both columns.

### 2. The clone is a distinct row in every other respect — passes today, keep it green

No `KnownGap`. Assert the clone has a different `Id`, the same `UserId` and `RoleId`, the request's
`Name`/`Text`/`CategoryId`, and that the source's own columns are unchanged. Partly redundant with the
existing tests, but it is the pin that stops the fix from being "construct a new `Activity` and forget
a field" — which is the obvious alternative implementation and the obvious way to break it.

### 3. Cloning an archived activity produces an active one

Covered as case 2 of [`QuickEditCloneArchived.md`](QuickEditCloneArchived.md) — do not duplicate it
here, but fix both in one change to `Clone()`.

## The fix, for reference

Either reset the two timestamps alongside `Id`:

```csharp
var cloned = (Activity)MemberwiseClone();
cloned.Id = 0;
cloned.CreatedTimestamp = cloned.ModifiedTimestamp = DateTime.UtcNow;
cloned.IsArchived = false;
return cloned;
```

or drop `MemberwiseClone` for an explicit object initialiser, which is the version that cannot silently
acquire a copied field when someone adds a column. The explicit version is preferable and is why case 2
above exists.

## Doc reconciliation

None. `../../docs/domain-map.md` already describes `Clone()` accurately as "a `MemberwiseClone` that
resets `Id` only" — if the implementation changes, that sentence changes with it.
