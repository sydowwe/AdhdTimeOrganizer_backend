# TEST-14 — a step's `Guid` must survive a PUT of its parent item

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

**Status today: zero tests.** `TodoListStepsCrudTests` drives the three step routes and
`TodoListItemPairingTests:123` drives the PUT, but nothing in the suite does a PUT **and then** uses a
step id. That gap is the entire reason `CQ-19` is invisible.

## The code under test

`AdhdTimeOrganizer.TodoLists/application/endpoint/todoListItem/command/UpdateTodoListItemEndpoint.cs:19-26`:

```csharp
if (req.Steps is not null)
    entity.Steps = req.Steps.Select(s => new TodoListStep
    {
        Name = s.Name,
        Order = s.Order,
        Note = s.Note,
        IsDone = s.Id.HasValue && entity.Steps.FirstOrDefault(e => e.Id == s.Id.Value)?.IsDone == true
    }).ToList();
```

`Id` is never assigned. `TodoListStep.Id` defaults to `Guid.NewGuid()`
(`domain/model/entity/TodoListStep.cs:5`) and the column is `ValueGeneratedNever()`
(`infrastructure/persistence/TodoListEntityConfigurationExtensions.cs:25`), so **every PUT mints a fresh
id for every step**. The incoming `StepRequest.Id` is read only to carry `IsDone` across, then dropped.

Steps are an owned type serialised `ToJson` into the item's row — there is no steps table, so this is
identity loss, not row churn. The ids matter because three routes take one in the path:

| Route | Endpoint |
|---|---|
| `PATCH /api/todo-list-item/{itemId}/steps/{stepId}/toggle` | `ToggleStepIsDoneTodoListItemEndpoint` |
| `PUT /api/todo-list-item/{itemId}/steps/{stepId}` | `UpdateStepTodoListItemEndpoint` |
| `DELETE /api/todo-list-item/{itemId}/steps/{stepId}` | `DeleteStepTodoListItemEndpoint` |

All three resolve the step with `Steps.FirstOrDefault(s => s.Id == stepId)` and 404 when it misses.

## Create `TodoListItemStepIdentityTests.cs`

Payload shape for the PUT, from `UpdateTodoListItemRequest` + `BaseUpdateTodoListRequest`:

```csharp
new {
    ActivityId = activityId,
    IsDone = false,
    DisplayOrder = displayOrder,          // echo what the item already has
    DoneCount = (int?)null,
    TotalCount = (int?)null,
    Note = "edited",
    SuggestedTime = (object?)null,
    TaskPriorityId = priorityId,
    DueDate = (DateOnly?)null, DueTime = (TimeOnly?)null,
    PairedLeisureActivityId = (long?)null,
    Steps = new[] { new { Id = stepId, Name = "Step one", Order = 1, Note = (string?)null } }
}
```

Route is `PUT /api/todo-list-item/{id}` (the framework `BaseUpdateEndpoint` convention — confirm
against `TodoListCrudAuthMatrixTests:257`, which already calls it).

Seed with `TodoListTestSeedHelper.SeedTodoListItemAsync(..., steps: [...], ...)`. Use a fixed
`TaskPriority` rank in an unclaimed band (e.g. 340).

### 1. **The core test — the id survives the PUT**

Seed an item with two steps. Read their ids out of the database. PUT the item back, **echoing both step
ids** in `Steps`. Then read the item on a fresh context.

Correct behaviour: the two step ids are unchanged.

```csharp
[Fact(DisplayName = "CQ-19: a step keeps its Guid across a PUT of its parent item -- three routes take that id in the path")]
[Trait("Status", "KnownGap")]
```

Today both ids are new. Assert on the id **set** (`stepsAfter.Select(s => s.Id).Should().BeEquivalentTo(idsBefore)`),
not on order — the fix may reconcile in place and the ordering is `Order`'s job, not the collection's.

### 2. **The consequence — the toggle route 404s after an edit**

The test that will make someone care. Seed an item with one step; capture its id. PUT the item (echoing
the step). Then `PATCH /api/todo-list-item/{itemId}/steps/{stepId}/toggle` with the **captured** id.

Correct behaviour: `204`, and the step reads back `IsDone = true`. Today: `404`, against a step the user
can see on screen.

Same `KnownGap` trait and `CQ-19` in the `DisplayName`. This is the strongest test in this file — it is
end-user behaviour, not an implementation detail, and it stays meaningful whatever shape the fix takes.

### 3. `IsDone` survives the round trip once, and only once

Seed an item with two steps, tick one through the toggle route. PUT the item echoing both ids — the
carry-over at `:25` should preserve `IsDone` on the ticked one. Assert that, then **PUT a second time**
echoing the ids the client received from the *first* response. Correct behaviour: the tick still
survives. Today it does not — the second PUT is matching against ids that no longer exist, so both steps
come back `IsDone = false`.

Write this one even if you fix `CQ-19` first; it is the regression pin for the carry-over logic itself.

### 4. A step created without an id gets one

PUT the item with a `Steps` entry whose `Id` is `null` (the "user added a line in the editor" case).
Correct behaviour: it is persisted with a fresh non-empty `Guid`, and the existing steps keep theirs.
Guards against a fix that swings the other way and writes `Guid.Empty`.

### 5. Omitting `Steps` entirely leaves them alone

PUT with `Steps = null`. `AfterMapping:19` skips the block, so the collection must come back untouched —
same ids, same `IsDone`, same count. Cheap, and it pins the one path through this method that is
currently correct.

## While you are in this file — `CQ-16`

The same PUT sets `IsDone` (`UpdateTodoListItemRequest.UpdateEntity:18`) and `DoneCount` (`:20`)
independently of the steps that `AfterMapping` then replaces, without going through
`BaseTodoListItem.SetDone`. So a PUT can also produce `IsDone = true` with every step unticked. If you
are writing scenario 1 anyway, add:

```csharp
[Fact(DisplayName = "CQ-16: a PUT cannot leave IsDone true while every step is open")]
[Trait("Status", "KnownGap")]
```

The fix for both findings is the same one line — end `AfterMapping` with `entity.SetDone(req.IsDone)`,
after the steps have been replaced — so the two tests go green together.

## Related findings

`CQ-19`, `CQ-16`, `DOC-6` (the create-with-steps path also sets `DoneCount = 0` on an item with no
`TotalCount`, which `summary.md:90-96` claims the API cannot do — cheap to assert in scenario 4 if you
are already there).
