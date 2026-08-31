# TEST-6 — `POST /api/todo-list-category/filter-sort`

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

**Status today: zero tests.**

## The endpoint

- **Route:** `POST /api/todo-list-category/filter-sort`
- **Source:** `AdhdTimeOrganizer.TodoLists/application/endpoint/todoListCategory/query/GetFilterSortTodoListCategoryEndpoint.cs`
- **Request:** `BaseFilterSortRequest<TodoListCategoryFilterRequest>` from
  `Sydowwe.Framework.application.dto.request.@base.table` — carries `UseFilter`, `Filter` and `SortBy`.
  The filter is `{ string? Name, bool HideEmpty }`. Read the framework request type for `SortBy`'s exact
  shape before writing payloads.
- **Response:** `List<TodoListCategoryResponse>` (200).
- **Custom endpoint**, not a framework base — unlike its sibling `FilterSortTodoListEndpoint`, which
  derives from `BaseFilterSortEndpoint` and overrides only `ApplyCustomFiltering` (that asymmetry is
  finding `CQ-8`).

## The one thing that makes it custom

After projecting and sorting, it appends a **synthetic row** when the user has any uncategorised list:

```csharp
new TodoListCategoryResponse { Id = -1, Name = "Other", Color = "default", Icon = "fas fa-ellipsis", Text = null }
```

`-1` is a sentinel that also appears, independently, in `TodoListFilterRequest.CategoryId`
(`dto/filter/ToDoListFilterRequest.cs:9`: "null = all, -1 = uncategorized, >0 = specific category") and
is consumed by `FilterSortTodoListEndpoint.cs:17-19`. Two DTOs and two endpoints encode the same magic
number with no shared constant. The tests below are what will stop the two halves drifting.

## Create `TodoListCategoryFilterSortTests.cs`

### 1. Returns the user's categories

Seed three categories. `POST` with `UseFilter = false`. Assert all three, and assert **no** `Id = -1`
row, because no uncategorised list exists yet. That second half is the whole point — the sentinel must
be conditional.

### 2. The `Other` row appears when an uncategorised list exists

Seed two categories, plus a `TodoList` with `CategoryId = null`. Assert the response has three entries
and that exactly one has `Id == -1`, `Name == "Other"`, `Color == "default"`,
`Icon == "fas fa-ellipsis"`.

Assert the literal values, not just the id. The frontend renders that icon and colour directly; a typo
here is invisible server-side.

### 3. The `Other` row is appended **after** sorting

Seed categories named "Zebra" and "Alpha" plus an uncategorised list, and sort by name ascending.
Correct/observed behaviour: `Alpha`, `Zebra`, `Other` — the sentinel is `result.Add(...)` at `:46`,
after `SortByMany`, so it always lands last regardless of the sort. Pin it. A future refactor to
`BaseFilterSortEndpoint` (`CQ-8`) is exactly the change that would move it, and a frontend that expects
"Other" last would break silently.

### 4. `HideEmpty` filters to categories that have lists

Seed category A with one list, category B with none. `POST` with
`UseFilter = true, Filter = { HideEmpty = true }`.

Assert A is present and B absent. Then repeat with `HideEmpty = false` and assert both — the guard at
`:29` is `req is { UseFilter: true, Filter.HideEmpty: true }`, so `UseFilter = false` with
`HideEmpty = true` must **not** filter. Test that combination explicitly; it is the arm a pattern-match
typo would break.

### 5. `HideEmpty` does not suppress the `Other` row

Seed only an uncategorised list and no non-empty categories, with `HideEmpty = true`. The `Other` row is
computed by a separate query (`:42-43`) that ignores `hideEmpty` entirely, so it still appears while the
real categories are filtered out. That is arguably correct — "Other" is never empty by definition — but
it is unstated. Pin it and say so in the `DisplayName`.

### 6. `Name` filter

Seed "Work", "Workout", "Home". Filter `Name = "Work"` → the first two.

Then the case-sensitivity pin: filter `Name = "work"` (lowercase). `:38` uses
`c.Name.Contains(req.Filter.Name)`, which Postgres translates to a case-**sensitive** `LIKE`, so this
returns nothing. Assert the empty result and name it in the `DisplayName` — this is the nit flagged for
all three name filters in the slice (`GridTaskPriorityEndpoint`, `FilterSortTodoListEndpoint` and this
one), and one recorded test beats three unrecorded observations.

### 7. `UseFilter = true` with `Filter = null`

`:37` guards with `req is { UseFilter: true, Filter: not null }`, so this must not throw. `:29`'s
pattern (`Filter.HideEmpty: true`) also null-checks by construction. One test for `200` and the full
list.

### 8. User scoping

Seed a category and an uncategorised list owned by `ReminderSeedHelper.OtherUserId`. Neither may appear,
and the `Other` row must **not** be triggered by the other user's uncategorised list. Both queries
(`:32` and `:42`) filter on `userId` explicitly — this is what proves the second one does too, which is
easy to forget in a refactor since it is a separate statement.

### 9. Auth

`401` unauthenticated; `200` for `CreateUserRoleClient()`.

### 10. Round-trip with `FilterSortTodoListEndpoint` — the sentinel contract

The integration test that makes the magic number real. Seed one categorised and one uncategorised list.

1. `POST /api/todo-list-category/filter-sort` → note the `Id = -1` row.
2. `POST /api/todo-list/filter-sort` with `Filter = { CategoryId = -1 }` → must return exactly the
   uncategorised list (`FilterSortTodoListEndpoint.cs:17-18` maps `-1` to `CategoryId == null`).
3. Same with the real category id → the categorised list.

This is the only test that ties the producer of the sentinel to its consumer. If `CQ-8`'s refactor
promotes `-1` to a shared constant, this test is what confirms both sides moved together.

## Related findings

`CQ-8` (this endpoint hand-rolls 56 lines of what `BaseFilterSortEndpoint` gives its sibling in 12),
`PERF-4` (the unconditional second round trip at `:42-43`, and the `EXISTS` at `:35` with no supporting
index), and the missing `todo_list(user_id, category_id)` index — scenarios 4 and 5 are its access
pattern. Also `DOC-` note: the `-1` sentinel is currently documented only in a DTO comment and belongs
in `domain-map.md`'s glossary.
