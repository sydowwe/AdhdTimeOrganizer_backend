# AdhdTimeOrganizer.TodoLists — 03 Risks & Rollout

Point-in-time snapshot, 2026-08-31. IDs reference [02-findings.md](02-findings.md) and
[01-testing.md](01-testing.md); nothing is restated here.

**Recheck, pass 2 (2026-08-31).** No code has changed in the slice since the first pass (last code
commit `e27158d`, 2026-08-19), so every risk below still stands. Changes: `CQ-19` enters the ranked list
at #9; `DOC-1` and `DOC-3` are resolved and drop out of row 19; the `CQ-2` boundary is the 102nd item,
not the 101st; and the `summary.md` line references in `MIG-5` / `MIG-7` are repaired — that file was
rewritten by today's doc commit `6079f93`.

## 11. Risk-ranked issue list

| # | Sev | Issue | ID | File |
|---|---|---|---|---|
| 1 | 🟠 | Merging two activities that each have an item on the same list violates the `(UserId, ActivityId, TodoListId)` unique index — the merge rolls back and the user can never complete it | `CQ-1` | `application/seam/TodoListActivityReferenceSource.cs:42` |
| 2 | 🟠 | The reorder rebalance builds an untranslatable LINQ expression (`GroupFilterExpression.Compile()` inside `Where`) and throws at runtime; the branch has never been executed by a test | `CQ-4` | `application/endpoint/BaseChangeDisplayOrderTodoListEndpoint.cs:152` |
| 3 | 🟠 | `GetNextDisplayOrder`'s `!= 0` sentinel treats an item at order 0 as an empty group — the 101st item in a priority group lands on 0, and every item after it collides back at the top | `CQ-2` | `infrastructure/persistence/TodoListExtensions.cs:17` |
| 4 | 🟠 | `POST /todo-list-item` accepts another user's `TodoListId` / `TaskPriorityId` unverified, where the Move endpoint explicitly checks them | `SEC-1` | `application/endpoint/todoListItem/command/CreateTodoListItemEndpoint.cs:27` |
| 5 | 🟠 | `PATCH /todo-list-item/{id}/priority` writes an unverified `TaskPriorityId` — same class of hole as #4, on an existing row | `SEC-2` | `application/endpoint/todoListItem/command/ChangePriorityTodoListItemEndpoint.cs:30` |
| 6 | 🟠 | `CalculateNewOrderAsync` recurses after a rebalance with no depth guard; the comment promises "retry once" and the `Summary` promises a 409 the code cannot produce | `CQ-5` | `application/endpoint/BaseChangeDisplayOrderTodoListEndpoint.cs:129` |
| 7 | 🟠 | Changing an item's priority moves it between `DisplayOrder` groups without recomputing the order — the item lands mid-list | `CQ-6` | `application/endpoint/todoListItem/command/ChangePriorityTodoListItemEndpoint.cs:28` |
| 8 | 🟠 | `dashboard-widget` bounds its 3-day window on server-local `DateTime.Today`, alone among this solution's dashboards | `CQ-18` | `application/endpoint/todoListItem/query/GetDashboardTodoListItemEndpoint.cs:13` |
| 9 | 🟠 | A PUT of an item mints a fresh `Guid` for every step, so the three `{stepId}` routes 404 against steps still on screen, and a second PUT silently unticks them | `CQ-19` | `application/endpoint/todoListItem/command/UpdateTodoListItemEndpoint.cs:20` |
| 10 | 🟠 | `MoveTodoListItemEndpoint` catches bare `Exception` (swallowing cancellation) and replaces its base's `HandleAsync`, losing the base's concurrency handling | `SEC-3` | `application/endpoint/todoListItem/command/MoveTodoListItemEndpoint.cs:70` |
| 11 | 🟠 | `GetNextDisplayOrder` casts a `bigint` column to `int` in SQL — `integer out of range` once orders leave `int`, and not sargable meanwhile | `CQ-3` | `infrastructure/persistence/TodoListExtensions.cs:17` |
| 12 | 🟠 | Domain-map invariant **3** ("per-user scoping is the DbContext's job, not the endpoints") is contradicted by the code in both directions, and as written licenses #4 and #5 | `DOC-5` | `docs/domain-map.md:154` |
| 13 | 🟠 | `MoveTodoListItemEndpoint` derives from `BasePatchEndpoint` and then re-implements everything it provides | `CQ-7` | `application/endpoint/todoListItem/command/MoveTodoListItemEndpoint.cs:15` |
| 14 | 🟠 | `GetFilterSortTodoListCategoryEndpoint` hand-rolls 56 lines of what its sibling gets from `BaseFilterSortEndpoint` in 12 | `CQ-8` | `application/endpoint/todoListCategory/query/GetFilterSortTodoListCategoryEndpoint.cs:11` |
| 15 | 🟠 | The whole ordering surface (`change-display-order`, `move`, `priority`, `GetNextDisplayOrder`) has one test, and it asserts a 404 | `TEST-1/2/3/10` | `01-testing.md` §8 |
| 16 | 🟡 | `GET /todo-list-item` is unpaged and projects the full activity graph per row | `PERF-1` | `application/endpoint/todoListItem/query/GetAllTodoListItemEndpoint.cs:33` |
| 17 | 🟡 | The rebalance issues one `UPDATE` per row on the request thread inside an open transaction | `PERF-2` | `application/endpoint/BaseChangeDisplayOrderTodoListEndpoint.cs:151` |
| 18 | 🟡 | Four indexes the slice's own query shapes require are missing | `PERF-3/4/6` | `02-findings.md` § "Indexes that should exist" |
| 19 | 🟡 | Client-settable `DisplayOrder` / `DoneCount` on the update DTO | `SEC-5` | `application/dto/request/base/BaseUpdateTodoListRequest.cs:8` |
| 20 | 🟡 | `SEC-4`, `CQ-9` … `CQ-17`, `DOC-2`, `DOC-4`, `DOC-6`, `PERF-5`, and the nits appendix | various | `02-findings.md` |

`DOC-1` and `DOC-3` were resolved by the doc commit `6079f93` and have left this table.

Nothing here is 🔴. There is no data-corruption or cross-user-read path in this slice: the auth/IDOR
matrix is complete for the plain CRUD routes, the global query filter holds on every read, and the two
integrity failures that do exist (#1, #3) fail loudly at the database rather than writing bad rows.

*Recheck qualifier on that last sentence:* #9 (`CQ-19`) is the one 🟠 that does **not** fail loudly — it
discards user-visible identifiers silently, and the second-PUT case loses a user's ticked steps with no
error anywhere. It is still not 🔴 (nothing is lost that the user cannot redo, and no other user's data
is involved), but it is the finding most likely to be reported as a bug by someone actually using the
app, and it is the cheapest of the 🟠s to fix — one line, shared with `CQ-16`.

## 10. Migration / rollout risks

| Risk | Likelihood | Mitigation | ID |
|---|---|---|---|
| Fixing `CQ-2` changes the value new items receive. A priority group that has reached 101 items has a live row at `DisplayOrder = 0`, and every item added after that one has collided at `DisplayOrderStart` — so a naive fix leaves duplicates the reorder endpoint cannot separate (it needs a gap). | Medium — reachable at 102 items in one group, and there is no cap enforcing anything | Ship the code fix and a one-off data migration together: `row_number()` over `(user_id, task_priority_id ORDER BY display_order DESC, id)` rewriting every group to a clean descending ladder from `DisplayOrderStart`. Run a `SELECT` first to size the affected set; if it is empty, the migration is a no-op and can be dropped. | `MIG-1` |
| Adding the four indexes in `PERF-3/4/6` locks `todo.todo_list_item` and `todo.todo_list` for the duration of the build. | High if done the default way | `CREATE INDEX CONCURRENTLY`, which cannot run inside a transaction — so each index needs its own migration with `migrationBuilder.Sql(..., suppressTransaction: true)`. **Schema-qualify every statement**: per `CLAUDE.md` hand-written SQL is not qualified for you, and these tables live in `todo`, not `public`. | `MIG-2` |
| `SEC-1`/`SEC-2` add ownership checks that reject FK ids which are currently accepted. If any production row already points at a foreign `TodoListId` or `TaskPriorityId`, the new check will not clean it up, and any *edit* of such a row will start failing. | Low — requires someone to have exercised the hole | Before shipping, run the detection query: items joined to `todo_list` / `task_priority` on a differing `user_id`, with `IgnoreQueryFilters`. Repair or null out what it finds. Do **not** add a DB-level composite FK for this — it would need `user_id` in both parent keys, which is a wider schema change than the bug warrants. | `MIG-3` |
| Fixing `CQ-7` properly means adding an async pre-mapping hook to `BasePatchEndpoint`, which lives in `framework/` — a **git submodule**. An uncommitted framework edit is invisible in the parent's `git status` (it shows only "modified: framework (new commits)") and will not travel with a push. | High, and it is the repo's standard trap | Commit and push in `framework/` **first**, then commit the new gitlink here. Every `BasePatchEndpoint` subclass across all seven slices recompiles — check `PlanningCrudAuthMatrixTests` and `TrackingMappingCrudAuthMatrixTests` stay green, not just this slice's. | `MIG-4` |
| The pinned FK constraint name `fk_planner_task_todo_list_items_todolist_item_id` is derived from `ApplyConfigurationsFromAssembly` ordering, which shifts whenever a slice moves. `docs/summary.md:119-124` says to expect more of these. | Medium — the split is finished, so ordering should now be stable, but any new module changes it again | Leave the pin. If a migration diff ever contains only a constraint rename, that is this mechanism, not a real change — pin the new name rather than accepting the rename. | `MIG-5` |
| **Drift risk: the six shared endpoint bases are subclassed from `AdhdTimeOrganizer.Routines`** (`BaseToggleIsDone`, `BaseToggleStepIsDone`, `BaseChangeDisplayOrder`, and the three step bases). `CQ-4`, `CQ-5`, `CQ-9` and `CQ-11` all live in those bases, so each fix changes Routines' behaviour without touching a Routines file. | Certain — it is the design | Re-run `RoutineTodoListResetJobHandlerTests`, `RoutineGroupedReadAndMoveItemTests` and `RoutineStreakFreezeTests` on every base change. `TodoListStepsCrudTests:161` already pins one shared base against `RoutineTodoList` — extend that pattern to the reorder base when fixing `CQ-4`. | `MIG-6` |
| **Drift risk: `GetNextDisplayOrder`'s arithmetic exists in five places** — the generic extension, the `TodoListItem` overload, and three inlined copies in the dev seeder (`CQ-13`) — plus a sixth, the `DbSet<RoutineTodoList>` overload that `docs/summary.md:53-56` places in Routines. `CQ-2`/`CQ-3` must be fixed in all of them. | High — a partial fix looks complete | Fix the extension, delete the seeder's copies in the same commit, and grep for `DisplayOrderStart` / `MinAsync` across the solution before closing. When the Routines overload migrates, it must pick up the fixed version. | `MIG-7` |
| `CQ-15` adds `IsManyWithOneUser()` to `TaskPriorityConfiguration`. If EF's conventions and the helper disagree on delete behaviour, this emits a real FK change on a populated table. | Low | Generate the migration and read it before applying. An empty diff means the call is now a guard; a non-empty one is a finding in its own right and needs the `Activity` FK family rules in `docs/claude/persistence.md` applied. | `MIG-8` |
| `CQ-12` deletes `TodoListGroupedByCategoryResponse` and `PERF-3` deletes `TodoList.ItemCount` / `CompletedCount`. Both are unreferenced in the backend, but the frontend is not in this repo. | Low | Neither is serialized by any endpoint today (`TodoListResponse` carries its own `ItemCount`/`CompletedCount`, projected in SQL), so the wire format is unchanged. Safe to delete; say so in the commit message. | `MIG-9` |

| **`CQ-19` has no data to migrate, but it may have a client working around it.** Persisted steps already carry distinct ids; the fix only stops new ones being minted on update. If the frontend currently re-fetches the item after every PUT (the natural workaround for ids that keep moving), it will keep working — but a client that *caches* the ids it sent will start hitting the step routes successfully for the first time. | Low | Ship it with `TEST-14` scenario 2 green. No migration, no wire-format change: `TodoListStepResponse.Id` is already serialised on every read, so the ids the client receives are simply stable now. Worth a line in the release note, because the observable change is "editing a task no longer breaks its checklist". | `MIG-10` |
| **`PERF-1` is a breaking wire change and ships in two repos.** `GET /todo-list-item` without `todoListId` was a 200 carrying every item the caller owned; it is now a 400. The matching frontend change (`fetchTodoListItems` takes a required id; the planner picker's cleared list filter fans out per list) is in `AntiProcrastinationApp-frontend`, a **separate repository** — so the backend can be deployed without it, and an old bundle's picker would start erroring on that one filter state. | Medium — one screen, one filter state | Deploy the frontend change with or before the API. Nothing else calls the route unfiltered: the home widget reads `dashboard-widget` and the list view always sends the id, both verified in that repo rather than assumed. No data migration. | `MIG-11` |

## Audit-log gaps

`CLAUDE.md` is explicit that auditing exists in Framework but is **not wired up in this app** — nothing
is written today, anywhere. So there is no per-finding audit gap to fix in this slice, and adding
`[AuditIgnore]` / `[NoAudit]` attributes now would be decorating a mechanism that does not run. What
follows is what a future wiring pass will need to know about TodoLists specifically.

| Observation | Consequence | ID |
|---|---|---|
| **No `ExecuteUpdateAsync` / `ExecuteDeleteAsync` anywhere in the slice.** Re-verified in the recheck across all 94 `.cs` files: the two textual hits are both comments (the interceptor's XML doc and `TodoListExtensions.cs:28`), not calls. `IgnoreQueryFilters` likewise appears only in that same comment. Every write goes through the ChangeTracker. | Good news twice over: it is why `TodoListItemCompletionInterceptor` cannot be bypassed today, and it means a future audit interceptor would capture 100% of this slice's writes with no remediation. **Preserve this** — in particular, do not take the obvious `ExecuteUpdateAsync` shortcut for `PERF-2` without the comment that finding calls for. | `AUDIT-1` |
| `TodoListItemSeeder.TruncateTable` (`infrastructure/persistence/seeder/dev/TodoListSeeder.cs:20-23`) calls `TruncateTableCascadeAsync<TodoListItem>()`, which bypasses the ChangeTracker, the interceptor and any future audit interceptor, and cascades to nothing this slice owns but *does* reach `ActivityHistory.TodoListItemId` (`SetNull`) and `PlannerTask.TodolistItemId`. | Dev-only (`IPerUserDevSeeder`; per `CLAUDE.md` only dev seeders truncate), so this is correct as designed. Flagged so a future audit pass explicitly exempts it rather than discovering it. | `AUDIT-2` |
| `TodoListItem.CompletedTimestamp` is last-write-wins: un-ticking an item nulls it (`TodoListItemCompletionInterceptor.cs:81`). There is no record that the item was ever completed, or when it was completed the first time. | The daily recap is therefore not reconstructable historically — un-tick a task and its past appearance in a recap is gone. That is a deliberate design choice (`TodoListItem.cs:36-39` argues the routine case is better served by `RoutinePeriodCompletion`), but it means **the recap cannot be used as an audit source** for streaks or reporting. If that is ever wanted, it needs an append-only completion table, not an audit interceptor. | `AUDIT-3` |
| The slice logs in exactly two places, both in the dev seeder (`:42`, `:48`, `:254`), all using `{UserId}` and counts. | No PII at any call site — compliant with `CLAUDE.md`'s logging rule. Nothing to fix. | `AUDIT-4` |
