# TEST-4 — Tests for the to-do list and routine endpoints

## Context you need (do not rediscover)

**Repo:** `C:/Users/jakub/RiderProjects/AdhdTimeOrganizer`

**Endpoints:** `AdhdTimeOrganizer/application/endpoint/todoList/` — subfolders `todoList/`,
`todoListCategory/`, `todoListItem/` (+ `todoListItem/steps/`), `taskPriority/`,
`routineTimePeriod/`, `routineTodoList/` (+ `steps/`), plus three shared bases directly in
`todoList/`:
- `BaseToggleIsDoneTodoListEndpoint.cs` — tick/untick for **both** to-do flavours; raises the IsDone
  events.
- `BaseChangeDisplayOrderTodoListEndpoint.cs` — reordering shared by items and routine items.
- `steps/BaseCreate|Update|DeleteStepEndpoint.cs` — checklist-step CRUD shared by both flavours.

**Entities:** `domain/model/entity/todoList/` — `BaseTodoListItem`, `TodoList`, `TodoListItem`,
`TodoListCategory`, `TodoListStep`, `TaskPriority`, `RoutineTimePeriod`, `RoutineToDoList`,
`RoutinePeriodCompletion`.

**Helper under the covers:** `infrastructure/persistence/extensions/TodoListExtensions.cs` —
`GetNextDisplayOrder`, `GetDisplayOrderById`, `GetGroupIdById`. The latter two take **only an id**
with no user predicate (`SEC-12`), relying entirely on the global query filter.

**Test project:** `AdhdTimeOrganizer.IntegrationTests`, new files under `Endpoints/`. Read
`Infrastructure/AppDbContextFixture.cs` and `Infrastructure/AuthTestBase.cs` first. xunit v3,
FluentAssertions, `[Collection("Postgres")]`. `CreateUserRoleClient()`, plus
`CreateFactory(roles, userId)` for a second user (caller disposes). `CreateDbContext()` +
`SeedAsync(db)`.

**Discover request/response shapes yourself** — DTOs under `application/dto/request/todoList/` (18
files) and `application/dto/response/todoList/` (11). Time-of-day in portal DTOs is **`TimeDto`**,
not `TimeOnly`.

Prefer subclassing the framework test bases in `framework/Sydowwe.Framework.Testing/baseTests/` for
the plain CRUD sets. **No portal endpoint subclasses them today** — you will be first; report
friction rather than hand-rolling silently.

## Domain rules (`docs/domain-map.md`)

**Uniqueness, all per user:** `TodoList(UserId, Name)`, `TodoListCategory(UserId, Name)`,
`RoutineTimePeriod(UserId, Text)` **and** `RoutineTimePeriod(UserId, LengthInDays)` — *two* indexes,
so a user may have only one period of each length. `TaskPriority(UserId, Priority)` — rank values are
unique, so reordering is a **swap**. `RoutineTodoList(UserId, TimePeriodId, ActivityId)` and
`TodoListItem(UserId, ActivityId, TodoListId)` — the same activity cannot be listed twice in one
list/period.

**Ranges:** `BaseTodoListItem` — `DoneCount >= 0`, `TotalCount` between **2 and 99**,
`DoneCount <= TotalCount`. `RoutineTimePeriod` — `LengthInDays` 1–365, `StreakThreshold` 1–100,
`StreakGraceDays` 0..`LengthInDays-1`, `HistoryDepth` 1–100, `ReminderLeadDays` NULL or
1..`LengthInDays-1`, `ResetAnchorDay` 1–7 when weekly-aligned (`≤7` or a multiple of 7) else 1–30.

**Delete behavior:** `RoutineTimePeriod` → `RoutineTodoList` **Restrict**; `TaskPriority` →
`TodoListItem` **Restrict**; `TodoListCategory` → `TodoList` **SetNull**.

**Steps** are owned JSON with `Guid` ids.

## Scenarios to write

### A. IDOR and auth matrix (write first)

Every `{id}` route across all six entity folders: as user B, attempt read/update/delete/toggle/reorder
against user A's ids; assert 404/403 and that user A's rows are unchanged in a fresh context.

Give **`BaseChangeDisplayOrderTodoListEndpoint` special attention**: reorder endpoints take *other*
ids as payload (the neighbour to reorder against), and `TodoListExtensions.GetDisplayOrderById` /
`GetGroupIdById` resolve those **without a user predicate**. If the global filter is ever bypassed on
that path, a user could probe or perturb another user's ordering. Explicitly test reordering user B's
item *against a target id belonging to user A*.

Auth matrix: unauthenticated → 401; `User` role → allowed; extension-client token → denied.

### B. `BaseToggleIsDoneTodoListEndpoint` and the `DoneCount` arithmetic

This base owns the tick/untick logic for both flavours and keeps `Steps[].IsDone` aligned with the
parent (`IsDoneLogic` / `ResetSteps`) — it is the **correct reference implementation** that
`PlannerTaskIsDoneChangedEventHandler` (`CQ-6`) fails to match.

- Toggle a step-counted item done → `DoneCount == TotalCount`, `IsDone == true`, **and every step
  ticked**.
- Toggle it undone → `DoneCount == 0`, `IsDone == false`, every step unticked.
- Tick steps one at a time → `DoneCount` increments; ticking the last one flips `IsDone` true.
- Untick one step of a fully-done item → `IsDone` false, `DoneCount == TotalCount - 1`.
- **Invariant fuzz:** `DoneCount` must never exceed `TotalCount` nor go below 0 across any sequence.
  `CQ-18` notes this invariant lives only in a DB check constraint and is re-derived in two handlers,
  so drive a randomized-but-seeded sequence of toggles and assert the invariant after each.

`CQ-19`: the check constraint is bypassed when either column is NULL (Postgres treats a NULL check as
satisfied). Try creating an item with `DoneCount` set and `TotalCount` null and record what happens.

### C. Steps CRUD (`BaseCreate|Update|DeleteStepEndpoint`)

Shared by both flavours, so test both. Steps are owned JSON with `Guid` ids:

- Adding a step updates `TotalCount` consistently (or assert whatever the intended relationship is —
  `TotalCount` is constrained 2..99, so adding a step to a 99-step item must fail cleanly).
- Deleting a done step adjusts `DoneCount`.
- A step id from **another user's item** must not be addressable.
- Deleting the second-to-last step of a 2-step item hits the `TotalCount >= 2` floor — assert a clean
  400/409, not a 500 from a raw check-constraint violation.

### D. Routine periods — the two unique indexes (`DOC-6` / `MIG-8`)

`RoutineTimePeriod` has **two** per-user unique indexes: `(UserId, Text)` and `(UserId, LengthInDays)`.

- Creating a second period with the same `Text` → clean 409.
- Creating a second period with the same `LengthInDays` but a different `Text` → **also** 409. This
  is the one people forget.
- Same values for a **different** user → succeeds.

Then check `infrastructure/persistence/seeder/userDefault/RoutineTimePeriodSeeder.cs`: per CLAUDE.md
its `Collides` must cover **both** indexes or sign-up seeding throws 23505. This was never verified —
add a test at the seeder level (mirror `Seeding/PerUserDefaultMatcherTests.cs`, which exists
precisely to keep this class of bug dead). **If `Collides` covers only one index, that is a finding.**

### E. Range constraints on `RoutineTimePeriod`

One test per boundary, asserting a clean 400 from the validator rather than a 500 from the DB:
`LengthInDays` 0 and 366; `StreakThreshold` 0 and 101; `StreakGraceDays == LengthInDays` (must fail,
max is `LengthInDays-1`); `ReminderLeadDays` on a **one-day period** (must fail — a one-day period can
never have a lead nudge); `ResetAnchorDay` 8 on a weekly-aligned period; `ResetAnchorDay` 31 on a
non-weekly one; `HistoryDepth` 0 and 101.

### F. `TaskPriority` reorder is a swap

`(UserId, Priority)` is unique, so a reorder must swap rather than insert — a naive implementation
throws 23505 mid-operation. Assert the swap completes and both rows end with the expected ranks.

### G. Delete behavior

- Deleting a `RoutineTimePeriod` that still has routine items → **Restrict**, clean 409.
- Deleting a `TaskPriority` still referenced by items → **Restrict**, clean 409.
- Deleting a `TodoListCategory` → **SetNull** on its lists; the lists survive with a null category.

### H. `GetCompletionHistoryRoutineTimePeriodEndpoint`

Returns `RoutinePeriodCompletion` rows bounded by `HistoryDepth`. Seed more completions than
`HistoryDepth` and assert the cap is applied and ordering is newest-first. IDOR-test it too.

### I. `GetAllGroupedRoutineTodoListEndpoint` and `MoveTodoListItemEndpoint`

- The grouped read is the routines screen's main query — assert grouping by period and user scoping.
  Note it also calls `RoutineResetService.TryReset` (see `CQ-4`): a read endpoint that mutates. Assert
  that reading does not skip a streak evaluation.
- `MoveTodoListItemEndpoint`: moving an item to a list already containing that activity must violate
  `TodoListItem(UserId, ActivityId, TodoListId)` → clean 409. Moving to **another user's** list must
  be refused.

## Conventions

- AAA; fresh `CreateDbContext()` for post-request assertions.
- Two distinct seeded users throughout.
- Ids and counts in assertions — never emails or names.
- If an IDOR test in **A** fails, that is a 🔴 finding: stop and report rather than weakening it.
