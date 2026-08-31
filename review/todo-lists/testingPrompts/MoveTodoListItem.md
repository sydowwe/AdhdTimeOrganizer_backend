# TEST-1 — `PATCH /api/todo-list-item/{id}/move`

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first — it has the file skeleton, the fixtures, the two
seeding traps and the KnownGap convention. Nothing below repeats it.

**Status today: zero tests.** This endpoint has no coverage of any kind.

## The endpoint

- **Route:** `PATCH /api/todo-list-item/{id:long}/move`
- **Source:** `AdhdTimeOrganizer.TodoLists/application/endpoint/todoListItem/command/MoveTodoListItemEndpoint.cs`
- **Request:** `MoveToListTodoListItemRequest { required long DestinationListId }` (body). The item id
  comes from the route, read via `Route<long>("id")`.
- **Validator:** `MoveToListTodoListItemValidator` — `DestinationListId > 0` only.
- **Response:** `204 No Content` on success; `404` if the item or the destination list is not found;
  `400` on validation failure; `409` mapped by hand from a `DbUpdateException` (see below).
- **Roles:** none declared, so the global configurator in `Program.cs:573-577` applies
  `User + Admin + Root`.

## What it does

1. `FindAsync` the item by route id → 404 if missing.
2. `AnyAsync` the destination `TodoList` **through the user-filtered context** → 404 if missing. The
   comment at `:51-53` states this doubles as the IDOR guard the validator does not provide.
3. Sets `TodoListId`, then recomputes `DisplayOrder` via
   `GetNextDisplayOrder(_settings, User.GetId(), e => e.TodoListId == destinationListId, ct)` — i.e. one
   gap below the current minimum in the destination list, or `DisplayOrderStart` if that list is empty.
4. Saves, wrapped in a `catch (Exception)` that maps through `DbUtils.HandleException` +
   `EndpointHelper.ToStatusCode`, specifically so a unique-index collision reports 409 rather than 500.

## Create `MoveTodoListItemTests.cs`

### 1. Happy path — the item moves and is re-ordered into the destination

Seed two lists, two priorities are not needed (one is fine), and an item on list A. Seed **two** items
already on list B with known `DisplayOrder`s (use distinct activities — see trap 1) so the destination
is not empty.

`PATCH /api/todo-list-item/{itemId}/move` with `{ DestinationListId = listBId }` → `204`.

Assert on a fresh context:
- `item.TodoListId == listBId`
- `item.DisplayOrder` is exactly `min(DisplayOrder of list B's other items) - DisplayOrderGap`.
  **Assert the value, not just "it changed"** — this is the only test that will pin
  `GetNextDisplayOrder`'s group-filter overload, and nothing else in the suite ever asserts a
  `DisplayOrder`.

### 2. Moving into an empty list gets `DisplayOrderStart`

Same shape, but list B has no items. Assert `item.DisplayOrder == settings.DisplayOrderStart`
(`100_000_000` with the shipped config). Resolve it from `IOptions<TodoListSettings>` rather than
hard-coding if the test host exposes services; otherwise hard-code with a comment naming
`appsettings.json`.

### 3. Unknown item id → 404, nothing written

`PATCH /api/todo-list-item/999999999/move`. Assert `404` and that no `TodoListItem` moved.

### 4. Unknown destination list id → 404, the item is untouched

Assert `404` **and** that the item's `TodoListId` and `DisplayOrder` are both unchanged — the endpoint
sets `TodoListId` before saving, so a fix that reorders those two steps would regress silently.

### 5. IDOR — another user's destination list is invisible

`ReminderSeedHelper.EnsureOtherUserAsync`, seed a list owned by `OtherUserId`, then try to move the
test user's item into it. Expect `404` (not 403 — every entity here is `IEntityWithUser` and the global
query filter makes a foreign row simply not exist). Assert with `IgnoreQueryFilters()` that the item
still sits on its original list.

### 6. IDOR — another user's item cannot be moved

Seed an item owned by `OtherUserId`, and a list owned by the test user. `PATCH` the foreign item id.

⚠ **This is the interesting one and it may not pass.** The item lookup at `:41` is
`FindAsync([Route<long>("id")], ct)`, and `FindAsync` returns a change-tracked entity without
re-querying if one is already tracked. Whether the global filter applies here is exactly what this test
establishes. Expect `404`; if you get `204`, that is a genuine finding — file it, assert the foreign
item was **not** modified (`IgnoreQueryFilters()`), and tag the test `[Trait("Status","KnownGap")]`
referencing `SEC-1`'s neighbourhood.

### 7. The hand-mapped 409 — the unique index collision

This is the case the `catch` block at `:70-78` exists for, and it is unverified. `todo_list_item` is
unique on `(UserId, ActivityId, TodoListId)`.

- Seed one activity, `activityX`.
- Seed item 1 on list A with `activityX`.
- Seed item 2 on list B with `activityX`. (Different lists, so no collision yet.)
- Move item 1 → list B. It would become `(user, activityX, listB)`, which item 2 already holds.

Assert **`409 Conflict`**, not 500, and assert on a fresh context that item 1 is still on list A. This
test is the whole justification for the endpoint's copied exception-mapping block; without it, someone
will "simplify" that block away.

### 8. `DestinationListId = 0` → 400

Trivially covers the validator being attached at all (`Validator<MoveToListTodoListItemValidator>()` in
`Configure`), which per this suite's own reasoning is a silent-failure mode.

## Related findings

`SEC-3` (bare `catch (Exception)` swallows `OperationCanceledException`; the endpoint replaces its
base's `HandleAsync`) and `CQ-7` (it derives from `BasePatchEndpoint` and re-implements everything the
base provides). Scenario 7 is the test that must stay green through any `CQ-7` refactor.
