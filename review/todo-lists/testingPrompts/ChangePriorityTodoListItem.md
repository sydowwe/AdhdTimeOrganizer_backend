# TEST-2 — `PATCH /api/todo-list-item/{id}/priority`

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

**Status today: zero tests.**

## The endpoint

- **Route:** `PATCH /api/todo-list-item/{id:long}/priority`
- **Source:** `AdhdTimeOrganizer.TodoLists/application/endpoint/todoListItem/command/ChangePriorityTodoListItemEndpoint.cs`
- **Base:** `BasePatchEndpoint<TodoListItem, ChangePriorityTodoListItemRequest>` — unlike the Move
  endpoint, this one actually uses its base and supplies only `Mapping`.
- **Request:** `ChangePriorityTodoListItemRequest { required long PriorityId }` (body).
- **Validator:** `ChangePriorityTodoListItemValidator` — `PriorityId > 0` only.
- **Response:** `204` / `404` / `400`.
- **Mapping:** `entity.TaskPriorityId = req.PriorityId;` — and nothing else.

## Why this needs tests beyond the happy path

`TaskPriorityId` is not just a label: it is the **grouping key for `DisplayOrder`**. See
`ChangeDisplayOrderTodoListItemEndpoint.cs:10` (`GroupFilterExpression => e.TaskPriorityId`) and the
`GetNextDisplayOrder(dbSet, settings, userId, taskPriorityId, ct)` overload used on create. Moving an
item between priorities is therefore a move between ordering groups — exactly what
`MoveTodoListItemEndpoint` handles correctly for lists, and what this endpoint does not handle at all
(finding `CQ-6`).

## Create `ChangePriorityTodoListItemTests.cs`

Use a fixed `TaskPriority` rank band no other file claims (existing files use 1–2, 700, and a random
1000–999000 range — pick e.g. 310–319 and say so in a comment).

### 1. Happy path

Seed two priorities and an item on the first. `PATCH` with the second's id → `204`. Assert on a fresh
context that `item.TaskPriorityId` is the second priority.

### 2. The item lands mid-list in its new group — `CQ-6`

The core test. Seed:
- priority **P1** with the item under test at some `DisplayOrder`, say `97_000_000`;
- priority **P2** with three items at `100_000_000`, `99_000_000`, `98_000_000` (distinct activities —
  see trap 1 in the shared context).

`PATCH` the item to P2.

Assert the **correct** behaviour: the item's `DisplayOrder` is one gap below P2's current minimum, i.e.
`97_000_000` is *coincidentally* right here — so **do not use 97_000_000**. Pick a starting order that
makes the bug visible: put the item at `99_500_000` in P1. Correct behaviour after the move is
`97_000_000` (bottom of P2); today's behaviour leaves it at `99_500_000`, which sorts it *between* P2's
first and second items.

```csharp
[Fact(DisplayName = "CQ-6: moving an item to another priority re-orders it into the destination group")]
[Trait("Status", "KnownGap")]
```

Assert the ordering the user would actually see: read P2's items ordered by `DisplayOrder` descending
and assert the moved item is **last**. That assertion survives any change to the exact arithmetic.

### 3. The vacated group keeps a consistent order

After scenario 2, assert P1's remaining items are still strictly ordered and none collide. Cheap, and it
catches a fix that reorders the destination by rewriting the source.

### 4. Unknown item id → 404

### 5. `PriorityId = 0` → 400 (validator is attached)

### 6. IDOR — another user's item

`ReminderSeedHelper.EnsureOtherUserAsync`; `PATCH` a foreign item id with a priority the caller owns.
Expect `404`. Assert with `IgnoreQueryFilters()` that the foreign item's `TaskPriorityId` is unchanged.

### 7. Cross-user `PriorityId` is accepted — `SEC-2`

The one that matters most. Seed a `TaskPriority` owned by `ReminderSeedHelper.OtherUserId`, then `PATCH`
the **caller's own** item with that foreign priority id.

Assert the **correct** behaviour — `404`, because a priority the caller cannot see should not be
assignable:

```csharp
[Fact(DisplayName = "SEC-2: a TaskPriorityId belonging to another user is rejected, not written")]
[Trait("Status", "KnownGap")]
```

Today this returns `204` and writes the foreign FK. Also assert the follow-on damage, because it is
what makes the severity concrete: after the patch, `GET /api/todo-list-item` must still return the
item. `TodoListItemResponse.Projection` projects `e.TaskPriority.Text` through a **required**
navigation that the global query filter excludes, so the read is expected to fail or to drop the row —
capture whichever it does in the assertion message.

### 8. `row_version` / concurrency

`BasePatchEndpoint` is used properly here, so unlike the Move endpoint this path should honour the
concurrency token. Worth one test if the base exposes it: load the item, patch it twice with a stale
token, expect the second to 409. Check `framework/Sydowwe.Framework/application/endpoint/base/command/BasePatchEndpoint.cs`
for whether the request carries `RowVersion` before writing this — if it does not, skip the scenario and
note it here rather than inventing one.

## Related findings

`SEC-2`, `CQ-6`. Scenario 2 is the regression pin for `CQ-6`; scenario 7 for `SEC-2`. If you are fixing
as well as testing, `MoveTodoListItemEndpoint.cs:54` and `:63-64` are the templates for both fixes.
