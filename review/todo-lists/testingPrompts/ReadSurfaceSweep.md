# TEST-7 (+ TEST-11, TEST-12) — the remaining uncovered read routes and two CRUD edges

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

One file covering five read routes with no behavioural test at all, plus two CRUD edges the auth matrix
does not reach. These are individually small; grouping them keeps the fixture setup in one place.

Suggested file: `AdhdTimeOrganizer.IntegrationTests/Endpoints/TodoListReadSurfaceTests.cs`, with a
nested class per route (the pattern `TodoListCrudAuthMatrixTests.cs` already uses).

Use a fixed `TaskPriority` rank band no other file claims (e.g. 380–389) and one activity per item
(trap 1 in the shared context).

---

## A. `POST /api/todo-list/filter-sort` — `FilterSortTodoListEndpoint`

`application/endpoint/todoList/query/FilterSortTodoListEndpoint.cs`. Derives from
`BaseFilterSortEndpoint<TodoList, TodoListResponse, TodoListFilterRequest>`; overrides only
`ApplyCustomFiltering`.

1. **`Name` filter** — seed "Work", "Workout", "Home"; filter `Name = "Work"` → two. Then lowercase
   `"work"` → empty (case-sensitive `LIKE`; pin it, same nit as the category endpoint).
2. **`CategoryId = -1` returns uncategorised lists only.** `:17-18` maps `-1` to `CategoryId == null`.
   This is the consumer half of the sentinel that `GetFilterSortTodoListCategoryEndpoint` produces —
   see scenario 10 of [`GetFilterSortTodoListCategory.md`](GetFilterSortTodoListCategory.md), which owns
   the round-trip test. Here, just pin the arm.
3. **`CategoryId = <real id>`** returns that category's lists only.
4. **`CategoryId = null`** returns all.
5. **`ItemCount` / `CompletedCount` are computed in SQL.** Seed a list with three items, two done.
   Assert `ItemCount == 3`, `CompletedCount == 2`. `TodoListResponse.Projection:23-24` runs two
   correlated subqueries for these; `PERF-3` proposes replacing them with one aggregate, and this is the
   test that has to stay green through that change. Also seed an empty list and assert `0`/`0`.
6. **`Category` projects, and its null arm.** One list with a category (assert
   `Category.{Name,Color,Icon,Text}`), one without (assert `Category == null` — `:25` has an explicit
   null guard nothing covers).
7. **User scoping** — a list owned by `ReminderSeedHelper.OtherUserId` must not appear.
8. `401` unauthenticated.

---

## B. `POST /api/task-priority/grid` — `GridTaskPriorityEndpoint`

`application/endpoint/taskPriority/query/GridTaskPriorityEndpoint.cs`. `BaseGridEndpoint`, four filter
arms in `ApplyCustomFiltering`, none tested.

Seed four priorities at ranks 381–384 with distinguishable `Text` and `Color`.

1. `Text` contains → matching subset. Plus the lowercase case-sensitivity pin.
2. `Color` **exact match** (`==`, not `Contains` — different from `Text`, worth pinning as-is).
3. `MinPriority` alone → `>= n`.
4. `MaxPriority` alone → `<= n`.
5. Both together → the closed range; and an inverted range (`Min > Max`) → empty, not an error.
6. All filters null → everything.
7. Paging: whatever `BaseGridEndpoint` exposes (page size / page number) returns the right slice and a
   correct total. Read the framework base for the response envelope before asserting.
8. User scoping + `401`.

---

## C. The three `all-options` routes

- `GET /api/todo-list/all-options` — `GetSelectOptionsTodoListEndpoint`, maps
  `new SelectOptionResponse(t.Id, t.Name)`.
- `GET /api/todo-list-category/all-options` — `GetSelectOptionsTodoListCategoryEndpoint`, maps
  `(c.Id, c.Name)` **and overrides `Sort` to order by `Name`**.
- `GET /api/task-priority/all-options` — `GetSelectOptionsTaskPriorityEndpoint`, maps
  `{ Id = e.Id, Text = e.Text }`.

One test each:

1. Returns the user's rows as `{ id, text }` pairs — assert both members, since two of the three use the
   positional constructor and one uses object-initialiser syntax; a silently swapped pair would be
   invisible to a count-only assertion.
2. **Category options are sorted by `Name`.** Seed "Zebra", "Alpha", "Mango"; assert alphabetical. That
   `Sort` override exists only on this one of the three, so it is the one that can regress.
3. **Task-priority options are *not* sorted by `Priority`.** `GetSelectOptionsTaskPriorityEndpoint`
   overrides only `Map`, so ordering falls back to whatever the base does. Seed ranks 384, 381, 383 and
   pin the observed order, naming it in the `DisplayName` — a priority picker that lists ranks out of
   order is a plausible product bug, and this test is what surfaces the question.
4. User scoping on all three; `401` on one as representative.

Note `GetSelectOptionsTaskPriorityEndpoint.cs:8` and `GetSelectOptionsTodoListEndpoint.cs:7` name their
constructor parameter `appDbContext`, the one name `docs/summary.md:34` says never appears in this
project. Cosmetic; mention it in the PR, don't test it.

---

## D. `GET /api/task-priority` — `GetAllTaskPriorityEndpoint`

Currently only the `NotBe(404)` smoke test. One behavioural test: returns the user's priorities with
`{Id, Text, Color, Priority}` projected (`TaskPriorityResponse.Projection`), scoped to the caller.

---

## TEST-11 — unique `(UserId, Name)` on `TodoList` and `TodoListCategory`

`TodoListConfiguration.cs:14` and `TodoListCategoryConfiguration.cs:14` both declare
`HasIndex(r => new { r.UserId, r.Name }).IsUnique()`. Nothing tests either.

`TaskPriorityReorderTests` establishes the expected shape for this codebase: a collision must return a
clean **409**, not a 500, and must not partially apply.

1. `POST /api/todo-list` twice with the same `Name` → second is `409`; assert only one row exists.
2. `PUT /api/todo-list/{id}` renaming onto a sibling's name → `409`; assert **both** rows unchanged.
3. Both again for `todo-list-category`.
4. The same name owned by `ReminderSeedHelper.OtherUserId` does **not** collide — the index is
   `(UserId, Name)`, so two users may each have a "Work" list. This is the arm that would break if
   someone ever "simplified" the index to `Name` alone.

---

## TEST-12 — `DELETE /api/todo-list/{id}` cascades its items

`TodoListConfiguration.cs:18-21` declares `OnDelete(DeleteBehavior.Cascade)` from `TodoList` to
`TodoListItem`. `TodoListDeleteBehaviorTests` covers the other three FKs in this area
(`RoutineTimePeriod` Restrict, `TaskPriority` Restrict, `TodoListCategory` SetNull) but not this one.

**Add it to `TodoListDeleteBehaviorTests.cs`,** not to the new file — that file's class comment already
frames itself as the delete-behaviour suite for this area, and this is the fourth FK it should have
covered.

1. Seed a list with two items; `DELETE` the list → `204`; assert both items are gone
   (`IgnoreQueryFilters()` on the assert, so a soft-delete or filter change cannot fake a pass).
2. Assert the items' `ActivityHistory` rows survive — `ActivityHistory.TodoListItemId` is `SetNull`, and
   `TodoListItemDailyRecapTests:316` already argues that recorded time must outlive the task. Cascading
   a list must not become a way to delete history. Reuse that file's recording helper.

---

## What is deliberately not here

- Paging on `GET /api/todo-list-item` — does not exist; see [`GetAllTodoListItem.md`](GetAllTodoListItem.md).
- `TaskPriority` create/update validation beyond the existing matrix — `TaskPriorityValidator` has a
  floor of 1 and no ceiling, and the DTO is `short` while the entity is `int` (`CQ-14`). Fix the type
  first; a test pinning a `short`-shaped API would have to be rewritten immediately after.

## Related findings

`PERF-3`, `PERF-4`, `PERF-5`, `CQ-8`, `CQ-14`, and the case-sensitive-`Contains` nit affecting all three
name filters.
