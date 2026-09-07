# TEST-6 — Read endpoints serve a malformed period (pins `CQ-5`, restores `DOC-4` / `DOC-7`)

> **DONE — 2026-09-07. Do not work this prompt again; everything below is kept for the reasoning only.**
>
> The bug this prompt describes **was fixed before the prompt was worked**, so the "fails today" notes
> are all stale. The fix landed as
> `AdhdTimeOrganizer.Routines/application/service/RoutineTimePeriodReadRefresher.cs` — the
> `PostProcess` hook the "Note on the fix" section predicted — and all four flat reads override
> `PostProcessItemsAsync` / `PostProcessAsync` to call it. `RoutineTodoListResponse.Projection` now
> carries `ReminderLeadDays` and the three freeze fields too.
>
> Coverage landed in **two** files, not the one this prompt names:
>
> - `Endpoints/RoutineFlatPeriodReadTests.cs` — `NextResetAt` / `CompletionHistory` on both period
>   routes, the elapsed-window refill **and** the open-window no-op, the lapsed-grace break **and** the
>   open-grace no-op, and the nested period on `GET /routine-todo-list`.
> - `Endpoints/RoutineReadEndpointShapeTests.cs` — what the above leaves open: the exact
>   `ComputeNextReset` oracle (rather than `NotBe(default)`, which a wrong anchor also passes), a
>   hand-set 1-of-2 freeze count surviving a read (the refill tests can only ever prove `2`, which is
>   also the default), the grace break from the `GET all` hook (the two endpoints override *different*
>   hooks, so neither implies the other), the item **by-id** route, select-options sorting, and the two
>   reorder tests.
>
> **One real defect came out of it, and it is not the one this prompt predicted.**
> `BaseChangeDisplayOrderTodoListEndpoint.RebalanceDisplayOrdersAsync` filtered with
> `Where(e => GroupFilterExpression.Compile()(e) == groupId)`. That puts a compiled delegate inside an
> expression tree; EF evaluated `Compile()` client-side, could not translate the resulting
> `Invoke(delegate, e)`, and threw — so **every reorder that had to rebalance answered 500**, on
> `todo-list-item` as well as `routine-todo-list`. Unreachable from any assertion that leaves a gap
> between neighbours, which is why nothing caught it. Fixed by composing the predicate expression
> (`InGroupExpression`); pinned by
> `ChangeDisplayOrder_WithNoGapBetweenNeighbours_RebalancesTheGroupAndCompletesTheMove`.
>
> Two fixture values below are impossible as written and were changed: the select-options lengths
> (30, 1, 7) and any second period sharing length 7 collide with the `(user_id, length_in_days)` unique
> index. The suite uses 1 / 5 / 30 and a 14-day second period.

Read `_SHARED-CONTEXT.md` first.

## Endpoints with no *shape* coverage

| Endpoint | File | Base |
|---|---|---|
| `GET /api/routine-time-period` | `application/endpoint/routineTimePeriod/query/GetAllRoutineTimePeriodEndpoint.cs` | `BaseGetAllEndpoint` |
| `GET /api/routine-time-period/{id}` | `.../GetByIdRoutineTimePeriodEndpoint.cs` | `BaseGetByIdEndpoint` |
| `GET /api/routine-time-period/all-options/` | `.../GetSelectOptionsRoutineTimePeriodEndpoint.cs` | `BaseGetSelectOptionsEndpoint` |
| `GET /api/routine-todo-list` | `application/endpoint/routineTodoList/query/GetAllRoutineTodoListEndpoint.cs` | `BaseGetAllEndpoint` |
| `GET /api/routine-todo-list/{id}` | `.../GetByIdRoutineTodoListEndpoint.cs` | `BaseGetByIdEndpoint` |

> **Route note.** `BaseGetSelectOptionsEndpoint` declares
> `Get($"/{entityName.Kebaberize()}/all-options/{AddedRouteParam}")` — the path is
> **`all-options`**, not `select-options`. Check the base rather than guessing; an earlier draft of
> this prompt had it wrong.

> **Auth is already covered, shape is not.** `TodoListCrudAuthMatrixTests` has
> `GetByIdRoutineTimePeriodEndpointTests` and `GetByIdRoutineTodoListEndpointTests`, both of which
> already assert that another user's id is not served. Do **not** duplicate that. Everything below is
> about what the body contains.

They are one-liners over framework bases, which is why they were skipped — and it is exactly why they
are broken.

## The bug being pinned

`application/dto/response/RoutineTimePeriodResponse.cs`. The static `From(period, historyAsc, now)`
(`:47-55`) exists so that the grouped read and the freeze endpoint cannot drift, and its doc says so.
But the framework bases call the raw `Projection` expression (`:57-78`) directly, and `Projection`
cannot compute the two derived members. So everything served through a base gets:

- `nextResetAt` = `0001-01-01T00:00:00` (`default(DateTime)`)
- `completionHistory` = `[]`

And `RoutineTodoListResponse.Projection` (`application/dto/response/RoutineToDoListResponse.cs:63-79`)
builds its nested period by hand and **omits four fields entirely** — `ReminderLeadDays` and all three
freeze fields — so they serialize as `reminderLeadDays: null`, `freezesRemaining: 0`,
`freezeBudget: 0`, `freezeBudgetResetsAt: null`.

The freeze one is a real client bug, not cosmetics. `RoutineTimePeriodResponse:23-27` documents the
contract: freezes are "always a number once the server supports freezes — the client reads `null` as
'this server has no freeze feature'". A hard `0` tells the SPA the user has **no freezes left** rather
than that the feature is absent, so it renders the affordance disabled instead of hidden.

It also breaks two documented rules at once:

- **Invariant 9** ("the freeze budget is refilled by whoever reads it, not by a job") — these endpoints
  display `FreezesRemaining` without ever calling `RoutineStreakFreezeService.RefreshFreezeBudget`, so
  the count can be stale as well as wrong. `summary.md` names three refreshing sites; these are a
  fourth and a fifth that do not.
- **The Grace rule** ("`CheckGrace` must run before `TryReset` on *every* access") — they also serve
  `Streak` straight from the row, so a period whose grace window lapsed yesterday still reports the
  pre-break streak until some other surface notices.

## What to write

New file `Endpoints/RoutineReadEndpointShapeTests.cs`.

Fixture: one `RoutineTimePeriod` for the default user with `LastResetAt` set to a known instant, a
non-default `HistoryDepth`, `FreezesRemaining = 1`, `FreezeBudget = 2`; two `RoutinePeriodCompletion`
rows; one `RoutineTodoList` on it. Model the seed on `RoutineStreakFreezeTests.SeedAsync(…)`
(`:38-64`), which already builds this shape.

Tests:

- `GetAll_ServesAComputedNextResetAt` — deserialize into `RoutineTimePeriodResponse` and assert
  `NextResetAt` equals `RoutineResetService.ComputeNextReset(period, now)`, or at minimum
  `.Should().NotBe(default)` and `.Should().BeAfter(new DateTime(2000,1,1))`. **Fails today.**
- `GetById_ServesAComputedNextResetAt` — same.
- `GetAll_ServesTheFreezeFields` — `FreezesRemaining == 1`, `FreezeBudget == 2`.
- `GetAll_RefreshesAnElapsedFreezeWindow` — set `FreezesRemaining = 0` and
  `FreezeBudgetResetsAt = UtcNow.AddDays(-1)` in the seed, call the endpoint, assert the response says
  `2` **and** that the refill was persisted (fresh `CreateDbContext()`, check the row). Copy the shape
  of `RoutineStreakFreezeTests.GroupedRead_ServesTheFreezeFields_AndRefillsAnElapsedWindow` (`:274`),
  which is the same assertion against the endpoint that gets it right — that test is your oracle.
- `GetAll_ServesALapsedGraceWindowAsABrokenStreak` — seed `Streak = 4` and
  `StreakGraceUntil = UtcNow.AddDays(-1)`, call `GET /api/routine-time-period`, assert the response
  reports `streak: 0`. **Fails today** — the read never calls `CheckGrace`. Verify through a fresh
  context that the break was persisted too, not just projected.
- `GetTodoList_NestedPeriod_CarriesTheFreezeFields` — `GET /api/routine-todo-list`, assert the nested
  `routineTimePeriod.freezesRemaining` is the real number, not `0`, and that `reminderLeadDays` round
  trips.
- `SelectOptions_AreSortedByLengthInDays` — the one thing these endpoints do customise
  (`GetSelectOptionsRoutineTimePeriodEndpoint.cs`, the `Sort` override). Seed three periods with
  lengths 30, 1, 7 and assert the order is 1, 7, 30. Cheap and currently unpinned. Remember the route
  is `/api/routine-time-period/all-options/`.
- `ChangeDisplayOrder_ReordersWithinThePeriodOnly` — while you are here.
  `ChangeDisplayOrderRoutineTodoListEndpoint` sets `GroupFilterExpression => e.TimePeriodId`, and the
  existing reorder tests all target `todo-list-item`, not this route
  (`PATCH /api/routine-todo-list/change-display-order`). Seed two periods with two items each, reorder
  inside one, assert the other period's orders are untouched.

## Note on the fix

Both bases expose the hook already, so no hand-written endpoint is needed:

- `BaseGetAllEndpoint` — `protected virtual Task<List<TResponse>> PostProcessItemsAsync(List<TResponse> items, CancellationToken ct)`
- `BaseGetByIdEndpoint` — `protected virtual Task<TResponse> PostProcessAsync(TResponse entity, CancellationToken ct)`

Both are documented for exactly this ("override when a response field can only be computed in C#, not
in SQL"). The wrinkle is that they receive the **response**, not the entity, so the override has to
re-read the periods (and their history) by id before calling `From(period, history, now)` — which is
also the natural place to run `RefreshFreezeBudget` and `CheckGrace` and save. Once fixed, invariant 9
in `domain-map.md` stands unchanged, which is the argument for fixing the code rather than amending the
doc.
