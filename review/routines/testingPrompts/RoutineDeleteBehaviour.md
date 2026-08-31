# TEST-12 — Delete behaviour on both routine entities

Read `_SHARED-CONTEXT.md` first. `Endpoints/TodoListDeleteBehaviorTests.cs` is the existing model in
this suite for exactly this kind of test — read it before writing.

## Endpoints

- `DELETE /api/routine-time-period/{id}` → `application/endpoint/routineTimePeriod/command/DeleteRoutineTimePeriodEndpoint.cs`
  (a bare `BaseDeleteEndpoint<RoutineTimePeriod>`)
- `DELETE /api/routine-todo-list/{id}` → `.../routineTodoList/command/DeleteRoutineTodoListEndpoint.cs`
  (a bare `BaseDeleteEndpoint<RoutineTodoList>`)

Neither has a test. Both are one-liners, so what is actually under test is the **FK delete behaviour
declared in the configurations** — and those are deliberately asymmetric.

## The delete graph

From `infrastructure/persistence/configuration/`:

| Relationship | Behaviour | Declared in |
|---|---|---|
| `RoutineTimePeriod` → `RoutineTodoList` | **Restrict** | `RoutineTimePeriodConfiguration.cs:74-77` and again in `RoutineToDoListConfiguration.cs:47-50` |
| `RoutineTimePeriod` → `RoutinePeriodCompletion` | **Cascade** | `RoutinePeriodCompletionConfiguration.cs:26-29` |
| `RoutineTodoList` → `TodoListStep` | inherited from `BaseTodoListConfigure()` — check it | `RoutineToDoListConfiguration.cs:17` |
| `Activity` → `RoutineTodoList` | via `IsManyWithOneActivity()` | `RoutineToDoListConfiguration.cs:45` |

The interesting pair is the first two: deleting a period **refuses** if it still has routine items, but
**silently destroys** its entire completion history. That is a defensible rule (the history is
meaningless without the period) but it is a permanent data loss triggered by a plain `DELETE`, and
nothing asserts either half.

CLAUDE.md flags "FK constraint names are derived from navigations" as a silent-failure mechanism, and
the relationship is declared **twice** for the Restrict edge (once from each side). If those two ever
disagree, one silently wins. That is the regression this file guards.

## What to write

New file `Endpoints/RoutineDeleteBehaviorTests.cs`.

### Time period

- `DeletePeriod_WithRoutineItems_IsRefused` — seed a period with one item, `DELETE`, assert a 4xx (409
  is the likely mapping via `DbUtils` → `DbForeignKeyError`; assert whatever the framework actually
  returns and say so) **and** that the period still exists.
- `DeletePeriod_WithNoItems_Succeeds_AndCascadesItsCompletionHistory` — seed a period with two
  `RoutinePeriodCompletion` rows and **no** items, `DELETE`, assert 204 and that both completion rows
  are gone. This is the data-loss half; pin it deliberately rather than discovering it later.
- `DeletePeriod_AfterItsItemsAreDeleted_Succeeds` — the realistic user flow: delete the items, then the
  period. Confirms Restrict is a sequencing constraint, not a dead end.

### Routine item

- `DeleteItem_RemovesItsSteps` — seed an item with three steps, `DELETE`, assert the steps are gone
  (whatever `BaseTodoListConfigure` declares — if it is Restrict rather than Cascade, assert *that*
  instead; read the helper first and write the test to match the declared behaviour, not to a guess).
- `DeleteItem_LeavesThePeriodAndItsHistoryIntact` — the containment check.
- `DeleteItem_LeavesTheActivityIntact` — a routine is a reference to an activity, not an owner of one.

### Cross-slice

- `DeleteItem_DoesNotStrandTheActivityMembershipSeam` — after deleting the only routine on an activity,
  `RoutineTodoListActivityMembershipSource.ActivityIds` must no longer return that activity id.
  `Modules/SeamWiringTests.cs` covers the seam's registration; this covers its content after a delete.
  Cheap, and the seam resolves by string key so its failures are silent (`domain-map.md` invariant 4).

### Auth

Both deletes on another user's row → 404, row still present. (Or leave to `RoutineAuthMatrix.md` — do
not write it twice.)

## Note

Check `ActivityForeignKeyInventoryTests.cs` first — it may already assert the `Activity` → routine FK
shape as part of the solution-wide inventory. If so, reference it rather than duplicating.
