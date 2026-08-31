# TEST-8 — `POST /api/todo-list-item` accepts another user's FK ids

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

**Status today:** `TodoListCrudAuthMatrixTests.cs:237` covers 401 / cross-user-404 / User-role for this
route. That matrix proves you cannot **read** another user's row. It does not prove you cannot **point
at one**, and that is the gap.

## The endpoint

- **Route:** `POST /api/todo-list-item`
- **Source:** `AdhdTimeOrganizer.TodoLists/application/endpoint/todoListItem/command/CreateTodoListItemEndpoint.cs`
- **Base:** `BaseCreateEndpoint<TodoListItem, CreateTodoListItemRequest>`, overriding `AfterMapping`.
- **Validator:** `CreateTodoListItemValidator` — `TaskPriorityId > 0`, `TodoListId > 0`,
  `TotalCount ∈ [2,99]`, `PairedLeisureActivityId > 0` and `≠ ActivityId`, `Note ≤ 1000`,
  `SuggestedTime` in range, per-step lengths. **No ownership check on any FK.**

`AfterMapping:27` does `entity.TodoListId = req.TodoListId;` and `CreateTodoListItemRequest.ToEntity`
copies `TaskPriorityId`, `ActivityId` and `PairedLeisureActivityId` straight through.

## Why the global query filter does not save this

The filter scopes **reads** of `IEntityWithUser` entities. This is a **write** of a raw `long` into an FK
column whose target row genuinely exists at the database level, so the FK constraint is satisfied and
nothing rejects it. `MoveTodoListItemEndpoint.cs:51-54` handles the identical situation correctly and
its comment says so explicitly — Create simply never got the same treatment.

Note that `docs/domain-map.md:84-87` invariant 2 currently *licenses* this ("per-user scoping is the
DbContext's job … not the endpoints"), which is finding `DOC-5`. Fixing the code without fixing that
sentence leaves the next person the same trap.

## Create `CreateTodoListItemForeignKeyOwnershipTests.cs`

Set up in every test:

```csharp
await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
long otherUserId = ReminderSeedHelper.OtherUserId;
```

Payload shape is in [`GetNextDisplayOrderBoundary.md`](GetNextDisplayOrderBoundary.md). Use a fixed
`TaskPriority` rank band (e.g. 340).

### 1. **A foreign `TodoListId` is rejected — `SEC-1`**

Seed a `TodoList` owned by `otherUserId`. `POST` a valid item owned by the caller, but with
`TodoListId = theirListId`.

Correct behaviour: `404` (matching how Move reports an invisible destination list), and **no row
created**.

```csharp
[Fact(DisplayName = "SEC-1: a TodoListId belonging to another user is rejected, not written")]
[Trait("Status", "KnownGap")]
```

Today: `201`/`200`, and the row is written. Assert with `IgnoreQueryFilters()` that no `TodoListItem`
exists with `TodoListId == theirListId`.

### 2. **A foreign `TaskPriorityId` is rejected — `SEC-1`**

Same shape with a `TaskPriority` owned by `otherUserId`. Correct behaviour: `404`, no row.

### 3. The downstream damage a foreign `TaskPriorityId` causes

This is what turns `SEC-1` from tidiness into a defect, so assert it explicitly.

Create the item with the foreign `TaskPriorityId` (i.e. exercise the bug), then `GET /api/todo-list-item`
as the caller. `TodoListItemResponse.Projection` (`dto/response/TodoListItemResponse.cs:60-66`) projects
`e.TaskPriority.Text` / `.Color` / `.Priority` through a **required** navigation that the global filter
excludes for that row.

Assert what actually happens — a 500, or the row silently missing from the list, or a null-populated
`TaskPriority`. Whichever it is, capture it in the assertion message. The user-visible symptom is "the
task I just created does not appear on my list", and that sentence belongs in the `DisplayName`.

Same probe for `GET /api/todo-list-item/{id}` (`GetByIdTodoListItemEndpoint`), which uses a different
code path into the same projection — `TodoListItemPairingTests:85-89` establishes that convention.

### 4. Non-existent ids still behave

`TodoListId = 999999999` and `TaskPriorityId = 999999999` (nothing owns them). Today these produce an FK
violation from Postgres; assert that it surfaces as a clean `404` or `409`, **not a 500**. If it is a
500, that is worth its own line in the findings — every other write path in this API maps
`DbUpdateException` through `DbUtils.HandleException`.

### 5. `ActivityId` — the same question, deliberately different answer

`ActivityId` is wired by `IsManyWithOneActivity()` and `Activity` is `IEntityWithUser` too, so a foreign
`ActivityId` is the same hole. Test it the same way.

**But note what is deliberately *not* checked:** `PairedLeisureActivityId` is intentionally unvalidated
beyond the FK and the self-pairing rule — `CreateTodoListItemValidator:25-32` explains why (this slice
cannot see `ActivityProfiles` to ask whether an activity is a leisure one; restricting the picker is the
frontend's job), and `TodoListItemPairingTests:25-27` pins that decision. **Do not "fix" pairing
validation.** Ownership is a different question from leisure-ness, though — if you add an ownership
check to `PairedLeisureActivityId`, keep the existing comment and extend it, don't replace it.

### 6. Positive control — the happy path still works

One test creating an item with all-owned FKs, asserting `201` and the row. Without it, a fix that
rejects *everything* passes scenarios 1–5.

## If you are fixing, not just testing

`AfterMapping` already returns `bool` — the `false` path exists for exactly this. Two `AnyAsync` calls
through the user-filtered context (`_dbContext.Set<TodoList>()`, `Set<TaskPriority>()`), 404 on either
miss. `MoveTodoListItemEndpoint.cs:54` is the template, comment and all.

`ChangePriorityTodoListItemEndpoint` has the same hole on an existing row (`SEC-2`) — see
[`ChangePriorityTodoListItem.md`](ChangePriorityTodoListItem.md) scenario 7. `CQ-7` proposes an async
pre-mapping hook on `BasePatchEndpoint` that would fix both plus `SEC-3` at once; that lives in the
`framework/` **submodule**, so read `MIG-4` in `03-risks-rollout.md` before starting.

Before shipping any of this to a live database, run the `MIG-3` detection query for rows that already
point across users.

## Related findings

`SEC-1`, `SEC-2`, `DOC-5`, `MIG-3`.
