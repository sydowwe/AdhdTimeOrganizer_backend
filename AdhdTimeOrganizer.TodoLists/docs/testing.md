# AdhdTimeOrganizer.TodoLists — Testing

## How to test this project

TodoLists has **no test project of its own**. Everything that tests it lives in
`AdhdTimeOrganizer.IntegrationTests`, and deliberately so: almost nothing this slice owns is
observable from a library-only unit test. The `DbContext` → `AppDbContext` alias, the global
user-scoping filter, the check constraints, the completion interceptor and the two seams are all
properties of the *composed* application.

Infrastructure comes from `Sydowwe.Framework.Testing`: `PostgresTestBase` + the `"Postgres"`
collection over `AppDbContextFixture` (a real Postgres container), and `UserRoleTestAuthHandler` for
role and ownership scenarios (`CreateUserRoleClient()`, `CreateAdminRoleClient()`). See the portal's
`docs/testing.md` and `docs/claude/testing.md` for the full setup.

**Bespoke to this area:** `TodoListTestSeedHelper` (`IntegrationTests/Endpoints/`) holds the fixtures
shared by the to-do and routine endpoint tests. Every item ultimately needs an `Activity`, so
activity seeding is delegated to `PlanningTestSeedHelper` rather than duplicated — reach for the
helper before writing new setup by hand.

## Strategy

**CRUD goes through the Framework base test classes.** `TodoListCrudAuthMatrixTests` subclasses
`BaseCreateEndpointTests` / `BaseUpdateEndpointTests` / `BaseDeleteEndpointTests` /
`BaseGetByIdEndpointTests` across all six to-do and routine entity folders in one file, covering 401
unauthenticated, 404-on-IDOR (not 403 — the query filter makes another user's row simply not exist),
and User-role access. Nothing here overrides `AllowedRoles()`, so `IsAdminOnly = false` throughout.
A new CRUD endpoint should be tested by adding a subclass there, not by hand.

**Everything this slice actually decides is hand-written**, because the interesting behaviour is
exactly what the bases don't know about: the step-counted done state machine, display-order
rebalancing, the completion stamp, pairing, and the recap join.

**Assert on rows, not on routes.** Nearly every mechanism here fails silently — a dropped interceptor
registration empties the recap, a Cascade slipping onto the pairing FK deletes tasks, a seam reading
the wrong column double-counts. `TodoListItemPairingTests` asserts the task **still exists** after the
paired activity is deleted, not that the column is null; that is the shape to copy.

**Pin what the code does, not what a spec assumed.** `TodoListStepsCrudTests` exists partly to record
that steps and `TotalCount` are *decoupled* — the spec assumed they interact. Where they diverged, the
tests document the actual behaviour in their `DisplayName`.

## The guards — do not delete these

Each one exists because the failure it catches produces no exception, no log line and no build error.

| Test | Pins |
|---|---|
| `TodoListsRouteSmokeTests.TodoListRoutes_AreRegistered` | The slice's assembly is in FastEndpoints' `o.Assemblies` — one route per family, including `TaskPriority`, the entity most likely to be left behind by a folder-driven move. Missing → every to-do route 404s |
| `TodoListItemDailyRecapTests.Completing_StampsTheTimestamp_AndUncompleting_ClearsIt` | `TodoListItemCompletionInterceptor` is registered and both directions work. Asserted on the column, so a failure points at the stamp rather than at the join |
| `TodoListItemDailyRecapTests.EditingAnAlreadyDoneItem_DoesNotRestampIt` | The transition-only guard. Without it, editing a note walks a finished task into today's recap |
| `TodoListItemDailyRecapTests.TimeIsAttributedByItem_NotByActivity` | `ITodoListItemLoggedTimeSource` keys off `ActivityHistory.TodoListItemId` only. Widening it to the activity looks like it can only find *more* and in fact double-counts |
| `TodoListItemDailyRecapTests` (`ZeroDay_ReturnsAnEmptyArray_NotNull`, `ItemCompletedWithoutARecording_AppearsWithZeroSeconds`) | The two ordinary-but-easily-broken payload shapes |
| `TodoListItemPairingTests.DeletingThePairedLeisureActivity_UnpairsTheTask_ButKeepsIt` | **SetNull, not Cascade.** If the FK is ever regenerated as required, deleting a leisure activity silently deletes every task bundled with it |
| `TodoListItemPairingTests.Create_RoundTripsThePairedActivityId_ThroughBothReads` | The id survives create and *both* read paths — a dropped projection is indistinguishable from "not paired" on the frontend |
| `TodoListItemPairingTests.{Create,Update}_RejectsPairingATaskWithItsOwnActivity` | The self-pairing rule, both entry points |
| `TodoListToggleAndDoneCountTests` (6 tests + `InvariantFuzz_DoneCountNeverEscapesBounds`) | Every branch of `IsDoneLogic`, including that `ForceValue == true` increments rather than jumping to done, and that the untoggle arm resets steps like every other branch |
| `TodoListToggleAndDoneCountTests.CheckConstraint_BypassedWhenTotalCountIsNull` | Records what the DB *actually* allows: `DoneCount` set with `TotalCount` NULL satisfies the check. Written against EF directly, because the API surface never produces it |
| `TodoListDeleteBehaviorTests` (3 tests) | The three cross-entity FKs: `TaskPriority` and `RoutineTimePeriod` 409 while items reference them; deleting a category SetNulls its lists |
| `TaskPriorityReorderTests` (2 tests) | `(UserId, Priority)` is unique and checked immediately — a naive PUT onto a live rank 409s and leaves both rows unchanged; the temporary-value swap is the supported path |
| `TodoListStepsCrudTests` (6 tests) | Step CRUD on **both** parents (`TodoListItem` and `RoutineTodoList`), so the shared bases are pinned for both; plus the decoupling from `TotalCount` and the foreign-item 404 |

Owned elsewhere but load-bearing for this slice:
`CoreRouteSmokeTests.CoreSeeders_AreRegisteredExactlyOnce` (the double-DI-scan guard, global for
every slice), `SeamWiringTests` (both of this slice's keyed seams resolve exactly once),
`ActivityMergeTests` and `ActivityForeignKeyInventoryTests` (the merge repoint and the frozen FK
inventory this slice contributes three FKs to), `CompletionFanOutTests` and
`ActivityTimeAutomationTests` (the Core completion events, asserted on rows), `ModuleSchemaTests`
(every entity resolves to the `todo` schema), `ModuleWiringTests`, and
`UserDataExportAndDeletionTests`.

## Known gaps (living list)

No test covering this slice is currently carried as an expected-failing `[Trait("Status","KnownGap")]`
item. Three standing caveats, none of them flakes:

- **The `DoneCount` / NULL-`TotalCount` check-constraint hole is a recorded fact, not a fixed bug.**
  `CheckConstraint_BypassedWhenTotalCountIsNull` documents that Postgres accepts the combination; the
  validators are what keep it unreachable through the API.
- **Steps have no floor or ceiling.** Deleting down to zero steps and creating past 99 both succeed;
  `TodoListStepsCrudTests` pins that as the intended behaviour, not a gap.
- **`UserDataExportAndDeletionTests.KnownGaps`** is a deliberately maintained list of user-scoped
  entities that are not exported. Adding a user-scoped entity to this slice fails that test until it
  is classified one way or the other — that is intended.
