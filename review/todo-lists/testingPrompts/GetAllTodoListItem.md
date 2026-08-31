# TEST-4 — `GET /api/todo-list-item`

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

**Status today: a route smoke test (`NotBe(404)`) and one incidental use** in
`TodoListItemPairingTests:85` that asserts a single projected field. The filter, the ordering and the
scoping are all unverified.

This is the route the main to-do view renders from, which makes it the highest-traffic uncovered read in
the slice.

## The endpoint

- **Route:** `GET /api/todo-list-item`
- **Source:** `AdhdTimeOrganizer.TodoLists/application/endpoint/todoListItem/query/GetAllTodoListItemEndpoint.cs`
- **Request:** `GetAllTodoListItemRequest { long? TodoListId }`, bound from the query string. Declared
  inline in the same file at `:9-12`.
- **Response:** `List<TodoListItemResponse>` (200).
- **Custom endpoint**, not a framework base — it hand-scopes with `.FilteredByUser(loggedUserId)`, which
  is the right thing and worth pinning.

```csharp
var query = dbContext.Set<TodoListItem>()
    .FilteredByUser(loggedUserId)
    .Where(tdl => req.TodoListId == null || tdl.TodoListId == req.TodoListId)
    .OrderBy(td => td.DisplayOrder);
```

## Create `GetAllTodoListItemTests.cs`

Fixed `TaskPriority` rank band (e.g. 370); one activity per item (trap 1).

### 1. No filter returns every item the user owns, across lists

Seed two lists with two items each. `GET /api/todo-list-item` → all four. Guards the
`req.TodoListId == null ||` short-circuit, which is the arm the to-do view uses on first load.

### 2. `?todoListId=` filters to one list

`GET /api/todo-list-item?todoListId={listA}` → exactly list A's two items. Assert the ids, not just the
count.

### 3. An unknown `todoListId` returns an empty array, not 404

`?todoListId=999999999` → `200` with `[]`. Pin it — a future "validate the list exists" change would be
a breaking API change for the frontend, and this test forces that to be a decision.

### 4. Another user's `todoListId` also returns empty

Seed a list with items owned by `ReminderSeedHelper.OtherUserId`, then request it by id as the test user.
`200` with `[]` — the `FilteredByUser` clause makes it empty rather than forbidden. Assert on
`IgnoreQueryFilters()` that the other user's items still exist, so an over-eager fix that deletes or
reassigns is caught.

### 5. Ordering is by `DisplayOrder` ascending

Seed three items with explicit `DisplayOrder` values out of creation order (e.g. `3_000_000`,
`1_000_000`, `2_000_000` — `SeedTodoListItemAsync` hard-codes `1000`, so set the column directly after
seeding, or extend the helper). Assert the response order.

⚠ **Check the sense against the frontend before asserting "top".** New items get progressively *lower*
`DisplayOrder` values (see `GetNextDisplayOrder`), so ascending order puts the newest **first**. Whether
that is intended is not documented anywhere; pin the observed behaviour and say in the `DisplayName`
that ascending `DisplayOrder` means newest-first, so the question is at least on the record.

### 6. Projection completeness — one assertion per branch

`TodoListItemResponse.Projection` (`dto/response/TodoListItemResponse.cs:17-71`) builds a five-level
graph. One test that seeds an item with everything populated and asserts every branch:

- `Activity.Name`, `Activity.Text`, `Activity.IsUnavoidable`
- `Activity.Role.{Name,Text,Color,Icon}` (required — a missing role would break the projection)
- `Activity.Category` populated **and**, in a second item, `null` (the projection has an explicit
  null-guard at `:36`; nothing covers the null arm)
- `TaskPriority.{Text,Color,Priority}`
- `Steps[]` with `{Id,Name,Order,IsDone,Note}` — ids must round-trip, since `TodoListStep.Id` is
  `ValueGeneratedNever` and the step-toggle route addresses steps by that Guid
- `DoneCount`, `TotalCount`, `Note`, `DueDate`, `DueTime`, `IsDone`, `DisplayOrder`,
  `PairedLeisureActivityId`

`TodoListItemPairingTests:18-19` makes the argument for why this matters: a field dropped from the
projection reads back as null and is indistinguishable from "not set". That reasoning applies to all of
them, not just the paired id.

### 7. Auth

`401` unauthenticated; `200` for `CreateUserRoleClient()`. As with the dashboard widget, roles come from
the global configurator rather than the endpoint, so one test records that.

### 8. `SuggestedTime` is absent from the response — record it

`TodoListItem.SuggestedTime` is an `IntTime`, is accepted on create and update, is validated
(`CreateTodoListItemValidator:38-41`), is persisted via `NullableIntTimeConverter`
(`ToDoListItemConfiguration.cs:19-20`) — and is **not in `TodoListItemResponse`**. Neither is
`CompletedTimestamp` (deliberately — the recap owns that) nor `TodoListId`.

Write it as an explicit pin, not a bug report: seed an item with a `SuggestedTime`, assert the response
JSON has no such property, and put the observation in the `DisplayName`
(`"SuggestedTime is accepted and stored but never projected — write-only today"`). Whether that is a
gap or intentional is a product question this review cannot settle; the test makes it visible.

## Note on `PERF-1`

This endpoint is unpaged and projects the full activity graph per row. Do not add a paging test — paging
does not exist yet, and a test asserting its absence would have to be deleted the day it is added.
Scenarios 1 and 2 are what a future paging change must keep green, which is the useful guard.

## Related findings

`PERF-1`, and the missing `(user_id, todo_list_id, display_order)` index in `02-findings.md`
§ "Indexes that should exist" — scenarios 2 and 5 are exactly its access pattern.
