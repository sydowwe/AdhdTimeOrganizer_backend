# TEST-6 — `BatchDeletePlannerTaskEndpoint`

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

## Why

`POST /api/planner-task/batch-delete` (`BaseBatchDeleteEndpoint`,
`application/endpoint/plannerTask/command/BatchDeletePlannerTaskEndpoint.cs`) has **no test**. Its
single-delete sibling has three (`PlanningCrudAuthMatrixTests`, `PlannerDeleteBehaviorTests`,
`ReminderRegistrationTests.DeletingTheTask_CancelsItsReminder`), and the same reminder-capture logic is
the reason both exist:

```csharp
protected override async Task BeforeDeleteAsync(IReadOnlyList<PlannerTask> entities, CancellationToken ct = default) =>
    _attachedReminderIds = await reminders.GetReminderIdsForPlannerTasksAsync(entities.Select(e => e.Id).ToList(), ct);

protected override Task AfterSave(IReadOnlyList<PlannerTask> entities, CancellationToken ct = default) =>
    reminders.CancelManyAsync(_attachedReminderIds, ct);
```

The reminder rows go with the tasks by FK cascade, which the Reminders **module** never sees — so the
ids must be captured before the delete and cancelled after the commit. If `BeforeDeleteAsync` is ever
dropped, the delete still returns 204 and the module keeps firing reminders for tasks that no longer
exist. Nothing catches that today.

`BatchDeleteTemplatePlannerTaskEndpointTests` in `PlanningCrudAuthMatrixTests` is the shape to copy for
the base-behaviour half, including its cross-user test
(`BatchDeleteTemplatePlannerTask_CrossUserId_Returns404NotForbidden`).

## Where

`AdhdTimeOrganizer.IntegrationTests/Endpoints/` — a region in `PlannerDeleteBehaviorTests`, which
already owns the delete graph, or a class of its own beside it.

## Scenarios

### A — happy path

Seed one calendar day and three planner tasks. `POST /api/planner-task/batch-delete` with all three ids.

Assert 204 (or whatever the base returns — check `BaseBatchDeleteEndpointTests`), and that all three rows
are gone while the **calendar row survives**. `planner_task.calendar_id` is `Restrict`, so deleting the
day's last task must not disturb the day.

### B — reminders are cancelled in the module

Attach reminders to two of the three tasks via `ReminderSeedHelper` (portal row **and** module
`ReminderDefinition`). Batch-delete all three.

Assert:

- Both portal `Reminder` rows are gone — FK cascade.
- Both module `ReminderDefinition`s for `ReminderRegistrationService.KeyFor(reminderId)` are
  **cancelled**. Copy the assertion from `ReminderRegistrationTests.DeletingTheTask_CancelsItsReminder`
  or `ApplyTemplatePlannerTaskConflictResolutionTests.AssertReminderCancelledAsync`.

This is the test that fails if `BeforeDeleteAsync` is removed, and it is the whole reason to write this
prompt.

### C — cross-user id in the list

Seed two tasks for user A and one for user B. As A, batch-delete all three ids.

`BaseBatchDeleteEndpoint` takes the count-mismatch path — the sibling template test asserts **404, not
403**. Assert that, and then the part that matters: **B's task still exists**, and A's two tasks are
**also still there** (the batch is refused whole, not partially applied). Also assert B's reminder, if
you gave their task one, is still registered.

### D — unknown id

Same shape with an id that does not exist at all. Same expectation as C — indistinguishable from a
foreign id, which is the intended behaviour.

### E — empty list

Whatever `BaseBatchDeleteEndpoint` does; pin it. `GetReminderIdsForPlannerTasksAsync` short-circuits on
an empty collection (`ReminderRegistrationService.cs:201-202`), so this should not reach the database.

### F — anonymous

401.
