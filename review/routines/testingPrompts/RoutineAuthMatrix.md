# TEST-9 — Auth / IDOR matrix for the untested endpoints

Read `_SHARED-CONTEXT.md` first.

## Why this exists

Eleven of the module's twenty-three endpoints have no test of any kind, and the ones that *are* tested
carry good IDOR coverage (`RoutineStreakFreezeTests.SpendFreeze_OnAnotherUsersPeriod_IsNotFound`,
`RoutinePeriodCompletionHistoryTests.History_ForeignPeriod_Returns404`,
`RoutineGroupedReadAndMoveItemTests.Move_ToAnotherUsersList_Returns404AndLeavesItemUnchanged`,
`UserRoutineSettingsTests.AnotherUsersDismissal_IsNotVisible` / `Unauthenticated_Returns401`). Copy
their shape.

The module's scoping story is `domain-map.md` **invariant 3**: per-user scoping is the DbContext's job
via the global `IEntityWithUser` filter, not the endpoints — `ApplyUserScoping` is a no-op virtual.
That is a legitimate design, but it means the protection is a single global mechanism with **no
per-endpoint safety net**, and query filters are compiled once and cached (CLAUDE.md's "silent
failures"). A regression in the filter would open every one of these routes at once and nothing would
fail loudly. This matrix is the guard for that.

Note the module is *not* uniform here (`CQ-12`): some endpoints add an explicit `UserId` predicate,
some override `AuthorizeAsync` to `true` with a comment, some rely on the filter silently. Test the
behaviour, not the mechanism — all three must produce a 404.

## Roles

Default `AllowedRoles()` is User + Admin + Root, which is correct for everything in this module —
there is no admin-only routine surface, and nothing here should call `IEndpoint.GetAdminRole()`. So the
role dimension is thin: assert only that an **unauthenticated** caller gets 401 and that a plain **User**
gets through. Do not invent role restrictions that the module does not have.

## What to write

New file `Endpoints/RoutineAuthMatrixTests.cs`. A `[Theory]` over the route table keeps this compact;
`Endpoints/TodoListCrudAuthMatrixTests.cs` is the existing model in this suite — read it first and
follow its structure rather than inventing a new one.

### Fixture

One period + one routine item + one step owned by `OtherUserId` (remember `EnsureOtherUserAsync`), and
the same trio owned by the default user as the positive control.

### Dimension 1 — unauthenticated → 401

Every route in the module. Use `CreateClient()` without auth (check how
`UserRoutineSettingsTests.Unauthenticated_Returns401` (`:128`) builds it and reuse that).

### Dimension 2 — authenticated but foreign resource → 404, and **the row is unchanged**

This is the dimension that matters; the status code alone is not enough for the mutating routes.

| Route | Foreign target | Also assert |
|---|---|---|
| `GET /routine-time-period/{id}` | other user's period | — |
| `PUT /routine-time-period/{id}` | other user's period | stored `Text` unchanged |
| `DELETE /routine-time-period/{id}` | other user's period | row still exists |
| `PATCH /routine-time-period/{id}/toggle-is-hidden` | other user's period | `IsHidden` unchanged |
| `GET /routine-time-period/{id}/completion-history` | covered already | — |
| `POST …/time-period/{id}/streak-freeze` | covered already | — |
| `GET /routine-todo-list/{id}` | other user's item | — |
| `PUT /routine-todo-list/{id}` | other user's item | stored `Note` unchanged |
| `DELETE /routine-todo-list/{id}` | other user's item | row still exists |
| `PATCH …/toggle-is-done` | other user's item id in `ids` | `IsDone` unchanged |
| `PATCH …/change-display-order` | other user's item | `DisplayOrder` unchanged |
| `POST/PUT/DELETE …/{itemId}/steps…` | other user's item | step count unchanged |

The step endpoints are the one place with an explicit predicate already —
`CreateStepRoutineTodoListEndpoint.GetParentQuery` filters `e.Id == itemId && e.UserId == userId`
(`:11-14`). Assert it anyway; the other two step endpoints should be checked for the same override.

### Dimension 3 — the leak that is not a 404

`GET /routine-time-period` and `GET /routine-todo-list` (list reads) must return **only** the caller's
rows. Assert the other user's seeded rows are absent from the payload, not just that the call
succeeded. Same for `select-options` and `grouped-by-time-period` (the last is already covered by
`RoutineGroupedReadAndMoveItemTests.GroupedRead_GroupsByPeriod_AndScopesToCallingUser`).

## Out of scope for this file

The **create** path's cross-tenant hole (`SEC-1`) is not an IDOR on `{id}` — it is a foreign
`timePeriodId` in a request body, and it belongs in `RoutineTodoListCrud.md`. Do not duplicate it here.
