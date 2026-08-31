# TEST-6 — Read endpoints serve a malformed period (pins `CQ-5`, restores `DOC-4`)

Read `_SHARED-CONTEXT.md` first.

## Endpoints with zero coverage

| Endpoint | File | Base |
|---|---|---|
| `GET /api/routine-time-period` | `application/endpoint/routineTimePeriod/query/GetAllRoutineTimePeriodEndpoint.cs` | `BaseGetAllEndpoint` |
| `GET /api/routine-time-period/{id}` | `.../GetByIdRoutineTimePeriodEndpoint.cs` | `BaseGetByIdEndpoint` |
| `GET /api/routine-time-period/select-options` | `.../GetSelectOptionsRoutineTimePeriodEndpoint.cs` | `BaseGetSelectOptionsEndpoint` |
| `GET /api/routine-todo-list` | `application/endpoint/routineTodoList/query/GetAllRoutineTodoListEndpoint.cs` | `BaseGetAllEndpoint` |
| `GET /api/routine-todo-list/{id}` | `.../GetByIdRoutineTodoListEndpoint.cs` | `BaseGetByIdEndpoint` |

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
builds its nested period by hand and **omits the three freeze fields entirely**, so they serialize as
`freezesRemaining: 0`, `freezeBudget: 0`, `freezeBudgetResetsAt: null`.

That last one is a real client bug, not cosmetics. `RoutineTimePeriodResponse:23-27` documents the
contract: freezes are "always a number once the server supports freezes — the client reads `null` as
'this server has no freeze feature'". A hard `0` tells the SPA the user has **no freezes left** rather
than that the feature is absent, so it renders the affordance disabled instead of hidden.

It also breaks `domain-map.md`'s **invariant 5**: these endpoints display `FreezesRemaining` without
ever calling `RoutineStreakFreezeService.RefreshFreezeBudget`, so the count can be stale as well as
wrong.

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
- `GetTodoList_NestedPeriod_CarriesTheFreezeFields` — `GET /api/routine-todo-list`, assert the nested
  `routineTimePeriod.freezesRemaining` is the real number, not `0`.
- `SelectOptions_AreSortedByLengthInDays` — the one thing these endpoints do customise
  (`GetSelectOptionsRoutineTimePeriodEndpoint.cs:11-14`). Seed three periods with lengths 30, 1, 7 and
  assert the order is 1, 7, 30. Cheap and currently unpinned.
- `GetById_ForAnotherUsersPeriod_IsNotFound` — both `GetById` endpoints override `AuthorizeAsync` to
  return `true` and rely entirely on the global query filter, with a comment saying so
  (`GetByIdRoutineTimePeriodEndpoint.cs:11-12`). That reliance is sanctioned by invariant 3 but is
  asserted nowhere. Seed a period for `OtherUserId` and expect 404. Do the same for
  `GET /api/routine-todo-list/{id}`.

## Note on the fix

The clean fix is to have the two `GetX` endpoints post-process through `From(period, history, now)`
rather than the bare `Projection`. If `BaseGetAllEndpoint` does not expose a hook for that, hand-write
them the way `GetAllGroupedRoutineTodoListEndpoint` already is — a custom endpoint is justified here
(CLAUDE.md's rule is that custom-ness needs a reason; "the projection cannot compute two of the
response's members" is one). Once fixed, invariant 5 in `domain-map.md` stands unchanged, which is the
argument for fixing the code rather than amending the doc.
