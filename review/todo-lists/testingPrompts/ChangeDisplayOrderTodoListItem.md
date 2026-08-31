# TEST-3 — `PATCH /api/todo-list-item/change-display-order`

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

**Status today: one test, and it asserts a 404.** `TodoListCrudAuthMatrixTests.cs:525`
(`ChangeDisplayOrderTodoListItemIdorTests`) proves user B cannot reorder against user A's item id.
Nothing else. **No test in this repo has ever produced a `DisplayOrder` and asserted its value**, and no
test has ever entered the rebalance branch — which is how `CQ-4` (an untranslatable LINQ expression that
throws at runtime) survived.

## The endpoint

- **Route:** `PATCH /api/todo-list-item/change-display-order`
- **Source:** `AdhdTimeOrganizer.TodoLists/application/endpoint/BaseChangeDisplayOrderTodoListEndpoint.cs`
  (generic base), concrete at
  `application/endpoint/todoListItem/command/ChangeDisplayOrderTodoListItemEndpoint.cs`, which supplies
  only `GroupFilterExpression => e.TaskPriorityId`.
- **Request:** `ChangeDisplayOrderRequest` from
  `Sydowwe.Framework.application.dto.request.@base` —
  `{ long MovedItemId, long? PrecedingItemId, long? FollowingItemId }`. Read the framework type to
  confirm the member names before writing payloads.
- **Response:** documented as `204` / `404` / `409` / `400` / `500` in the `Summary` block at `:30-39`.
  **The 409 is advertised and unreachable** — see scenario 6.
- **Same base is subclassed from `AdhdTimeOrganizer.Routines` for `RoutineTodoList`**, so every test here
  has a Routines twin. Add at least one (scenario 8).

## The arithmetic, so your assertions can be exact

`ArithmeticNewOrderNumber(pre, fol) = pre + (fol - pre) / 2` (integer division). Config is
`DisplayOrderGap = 1_000_000`, `DisplayOrderStart = 100_000_000`. The three cases in
`CalculateNewOrderAsync`:

| Request shape | Result |
|---|---|
| no preceding, no following | `DisplayOrderGap` (**note: not `DisplayOrderStart`** — see the nits appendix in `02-findings.md`) |
| no preceding, following = F | midpoint of `(F - gap, F)` |
| preceding = P, no following | midpoint of `(P, P + gap)` |
| both | midpoint of `(P, F)`; if the midpoint equals either endpoint → rebalance, then recurse |

Note the naming: `PrecedingItemId` is the item with the *lower* `DisplayOrder` and `FollowingItemId` the
higher one — the list is rendered **descending** by `DisplayOrder` (`GetAllTodoListItemEndpoint` orders
ascending, but new items get progressively *lower* orders, so the newest sorts last). Verify this
against the frontend's payload before asserting "top" and "bottom"; if the sense is inverted, the tests
still hold, only the prose changes.

## Create `ChangeDisplayOrderTodoListItemTests.cs`

Seed a single `TaskPriority` (fixed rank, e.g. 320) and put every item in it, so they share one
`GroupFilterExpression` group. Distinct activities per item (trap 1).

### 1. Move between two neighbours — the exact midpoint

Three items at `100_000_000`, `98_000_000`, `96_000_000`. Move the third between the first two:
`{ MovedItemId = third, PrecedingItemId = second, FollowingItemId = first }`.

→ `204`, and the moved item's `DisplayOrder` is exactly `99_000_000`. **Assert the number.**

### 2. Move to the top

`{ MovedItemId = third, PrecedingItemId = null, FollowingItemId = first }` where first is at
`100_000_000` → midpoint of `(99_000_000, 100_000_000)` = `99_500_000`. Assert the number, then assert
the rendered order (all three by `DisplayOrder`) puts the moved item first.

### 3. Move to the bottom

`{ PrecedingItemId = last, FollowingItemId = null }` → midpoint of `(L, L + gap)`.

### 4. Both ids null — the inconsistent first-item value

`{ MovedItemId = only, PrecedingItemId = null, FollowingItemId = null }` → the code returns
`DisplayOrderGap` (`1_000_000`), where item *creation* uses `DisplayOrderStart` (`100_000_000`) for the
same "first item" situation. Pin what it does today and name the inconsistency in the `DisplayName`; this
is a 🟡 nit, not a defect, and the pin is what will make someone reconcile the two.

### 5. Unknown `PrecedingItemId` / `FollowingItemId` → 404, and the moved item is untouched

Two facts. The endpoint has distinct error messages for each ("The specified 'FollowingItemId' was not
found.") — assert on the status only, and assert the moved item's order is unchanged.

### 6. **The rebalance branch — `CQ-4` and `CQ-5`**

The reason this file exists. Reaching it requires two adjacent items whose orders differ by 1, so the
midpoint equals an endpoint.

Seed directly through `CreateDbContext()` (do not try to reach this state through the API — it takes
~20 successive reorders):

- item A at `50_000_001`
- item B at `50_000_000`
- item C anywhere else in the same priority group

Then `PATCH { MovedItemId = C, PrecedingItemId = B, FollowingItemId = A }`. Midpoint of
`(50_000_000, 50_000_001)` is `50_000_000` — equal to `preOrder` — so `CalculateNewOrderAsync:130`
fires `RebalanceDisplayOrdersAsync`.

Assert the **correct** behaviour:

```csharp
[Fact(DisplayName = "CQ-4: exhausting the gap between two neighbours rebalances the group and completes the move")]
[Trait("Status", "KnownGap")]
```

- `204`
- every item in the priority group has a distinct `DisplayOrder`
- consecutive orders differ by exactly `DisplayOrderGap`
- the top of the group is `DisplayOrderStart - DisplayOrderGap` (per `:157`)
- the moved item sits between B and A in the rendered order

Today this returns **500**: `GroupFilterExpression.Compile()` inside an EF `Where` (`:152`) cannot be
translated. Capture the response body in the failure message so the next reader sees
`The LINQ expression ... could not be translated` rather than a bare status mismatch.

**Second fact, for `CQ-5`:** after fixing `CQ-4`, force a rebalance that cannot create a gap — pass a
`FollowingItemId` whose `GetGroupIdById` returns null so `RebalanceDisplayOrdersAsync` returns early at
`:146-147`, then `CalculateNewOrderAsync` recurses with identical arguments. Correct behaviour is the
**409** the `Summary` block already advertises. Today's behaviour once `CQ-4` is fixed is unbounded
recursion — so **write this test before shipping the `CQ-4` fix**, or the fix turns a 500 into a stack
overflow.

### 7. Reordering does not touch `IsDone` or `CompletedTimestamp`

Seed a done item with a known `CompletedTimestamp`, reorder it, assert both are unchanged. Cheap, and it
pins that `PERF-2`'s eventual `ExecuteUpdateAsync` optimisation (which bypasses
`TodoListItemCompletionInterceptor`) stays safe.

### 8. The same base behaves identically for `RoutineTodoList`

Mirror `TodoListStepsCrudTests:161`'s pattern: one scenario-1-shaped test against
`PATCH /api/routine-todo-list/change-display-order`, seeding with
`TodoListTestSeedHelper.SeedRoutineTimePeriodAsync` + `SeedRoutineTodoListAsync`. Routines'
`GroupFilterExpression` is `TimePeriodId`, so seed all items in one period. This is the guard that stops
a `CQ-4`/`CQ-5` fix from being validated only on the TodoLists side.

## Related findings

`CQ-4`, `CQ-5`, `SEC-4` (the moved-item lookup omits the `userId` predicate its sibling helpers apply),
`PERF-2` (the rebalance is one `UPDATE` per row on the request thread). Scenario 6 is the highest-value
test in this whole review.
