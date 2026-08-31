# TEST-10 (+ TEST-13) — `GetNextDisplayOrder` boundary, and the dev seeder's copies of it

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

**Status today: zero tests.** Nothing asserts a `DisplayOrder` value anywhere in the suite, and
`TodoListSettings` binding is unverified end to end.

## The code under test

`AdhdTimeOrganizer.TodoLists/infrastructure/persistence/TodoListExtensions.cs:9-24`:

```csharp
var lastOrder = await query.MinAsync(e => (int?)e.DisplayOrder, ct) ?? 0;
return lastOrder != 0 ? lastOrder - settings.DisplayOrderGap : settings.DisplayOrderStart;
```

Two defects in two lines:

- **`CQ-2`** — `MinAsync` over `int?` already returns `null` for an empty group; `?? 0` discards that
  signal for a magic number that is a legal `DisplayOrder`. So "the group is empty" and "the lowest item
  sits at 0" are indistinguishable.
- **`CQ-3`** — `(int?)` casts a `bigint` column, producing `CAST(display_order AS integer)` in SQL. Not
  sargable, and it raises `integer out of range` once any order leaves `int` range.

Config (`appsettings.json:11-13`): `MaxItems: 100`, `DisplayOrderGap: 1_000_000`, so
`DisplayOrderStart = MaxItems * DisplayOrderGap = 100_000_000`.

**The boundary is reachable by construction.** Items descend one gap at a time from `100_000_000`, so
item *n* lands on `100_000_000 - (n-1) × 1_000_000`. The **101st** item lands on exactly `0` — still
correct. The **102nd** is the broken one: the group minimum is now `0`, the sentinel misfires, and it is
handed `100_000_000`, colliding with item 1. Nothing enforces `MaxItems` as a cap.

> ⚠ Corrected in the review's second pass — the earlier text said the 101st item resets. It does not;
> the 101st is the one that legitimately lands on `0`, and the 102nd is where the reset happens. Assert
> against the 102nd, or use the one-row reproduction in §4, which is unaffected.

## Create `TodoListItemDisplayOrderTests.cs`

Everything here goes through `POST /api/todo-list-item`, because that is the only production caller of
the `taskPriorityId` overload. Payload shape from `CreateTodoListItemRequest`:

```csharp
new {
    ActivityId = activityId,        // from ActivityIdRequest
    TaskPriorityId = priorityId,
    TodoListId = listId,
    TotalCount = (int?)null, Note = (string?)null, SuggestedTime = (object?)null, Steps = (object?)null,
    DueDate = (DateOnly?)null, DueTime = (TimeOnly?)null, PairedLeisureActivityId = (long?)null
}
```

Trap 1 applies hard here: `(UserId, ActivityId, TodoListId)` is unique, so **every created item needs its
own activity**. Seed them in one batch via `PlanningTestSeedHelper.SeedActivityAsync` before the loop.
Use a fixed `TaskPriority` rank in an unclaimed band (e.g. 330).

### 1. The first item in an empty group gets `DisplayOrderStart`

Create one item. Assert `DisplayOrder == 100_000_000`. Read the value from
`IOptions<TodoListSettings>` if the test host exposes services; otherwise hard-code and name
`appsettings.json` in a comment. **This is also the only test that proves `TodoListSettings` is bound at
all** — `Program.cs:595-596` does `services.Configure<TodoListSettings>(configuration.GetSection(...))`,
and a typo'd section name yields `MaxItems = 0`, `DisplayOrderGap = 0`, i.e. every item at order 0.
Say that in the `DisplayName`.

### 2. Successive items descend by exactly one gap

Create three items in one priority group. Assert `100_000_000`, `99_000_000`, `98_000_000` in creation
order.

### 3. Items in *different* priority groups do not interact

Create an item in priority A and one in priority B. Both must get `DisplayOrderStart` — the group filter
is `TaskPriorityId`. Guards against someone "simplifying" the overload away.

### 4. **The item after the ladder reaches zero — `CQ-2`**

The core test. **Write the one-row version** — it isolates the sentinel exactly and costs one seed row:

Seed a single item in a fresh priority group with `DisplayOrder = 0`, then create one **through the
API** so `GetNextDisplayOrder` actually runs.

Correct behaviour: the new item gets `-1_000_000`. Negative orders are fine — the column is `bigint`
and the ladder is descending by design. Today it gets `100_000_000`, because `?? 0` makes "the group is
empty" and "the lowest item sits at 0" the same signal.

```csharp
[Fact(DisplayName = "CQ-2: an item created into a group whose lowest DisplayOrder is 0 continues the ladder instead of resetting to DisplayOrderStart")]
[Trait("Status", "KnownGap")]
```

Assert the strongest form available: **every item in the group has a distinct `DisplayOrder`** after the
create. That survives any change to the arithmetic and is the property the user actually cares about.

The full-scale version, if you want the boundary itself on record: seed 101 rows at `100_000_000` down
to `0`, then create the 102nd through the API — it must get `-1_000_000`. (101 items need 101
activities, because of trap 1; generate them in one `AddRange` + a single `SaveChangesAsync`.) This is
slow and asserts nothing the one-row version doesn't, so treat it as optional.

### 5. **`CQ-3` — the `int` cast**

Seed one item in a fresh priority group with `DisplayOrder = 3_000_000_000` (above `int.MaxValue`,
comfortably inside `bigint`). Then create an item through the API.

Correct behaviour: `204`/`200` and the new item at `2_999_000_000`. Today: Postgres raises
`integer out of range` from `CAST(display_order AS integer)` and the create fails.

```csharp
[Fact(DisplayName = "CQ-3: DisplayOrder is a bigint and must not be cast to int in the MIN projection")]
[Trait("Status", "KnownGap")]
```

Do the same for the negative side (`-3_000_000_000`) if you want both bounds; one is enough to pin the
cast.

### 6. `MoveTodoListItemEndpoint` uses the *generic* overload

`GetNextDisplayOrder(settings, userId, e => e.TodoListId == destinationListId, ct)` — a different
overload with the same body. One assertion is enough here since `MoveTodoListItemTests` scenarios 1–2
cover it; cross-reference rather than duplicating.

## TEST-13 — the dev seeder's three inlined copies

`infrastructure/persistence/seeder/dev/TodoListSeeder.cs:80-93` reimplements the same arithmetic three
times (once per priority), carrying both `CQ-2` and `CQ-3` into three more locations. A fix to the
extension will silently not reach them.

Rather than writing an integration test for a dev-only seeder, **write a guard**: a test that asserts the
seeder file contains no `MinAsync` — i.e. that it calls the extension. That is the shape
`Infrastructure/ModuleSchemaTests` and `Modules/ModuleWiringTests` already use for
"mechanism that must not be re-implemented" checks; copy the nearest one. If a source-text assertion is
too blunt for this codebase's taste, the alternative is to make the seeder's next-order computation an
internal method and unit-test it — but the guard is cheaper and catches the actual regression, which is
divergence rather than wrongness.

While you are there: the class is `TodoListItemSeeder` in a file named `TodoListSeeder.cs`.
`docs/domain-map.md:142` now documents that mismatch rather than getting it wrong; renaming the file
lets that clause go. See `CQ-13` and `DOC-2`.

## Related findings

`CQ-2`, `CQ-3`, `CQ-13`, `PERF-6`, `MIG-1` (fixing `CQ-2` needs a backfill for any group already past
100 items — read the rollout table before shipping the fix), `MIG-7` (the arithmetic exists in five
places).
