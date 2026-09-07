# TEST-17 — carving an overnight task must not silently delete it

> ## ✅ DONE 2026-09-08 — both halves of "the fix this test drives" landed
>
> The test is
> `ApplyTemplatePlannerTaskConflictResolutionTests.MergeOverwrite_LeavesANegativeDurationTaskWhole_InsteadOfDeletingIt`.
> It follows the scenario below with two deviations, both anticipated in the prompt itself:
>
> - **The overnight row is seeded, not posted.** `CQ-14` was fixed in the same change, so
>   `POST /api/planner-task` now answers 400 for a 23:00→01:00 span — as step 2 said to do if that
>   happened. The carving defect is independent of how the row got there.
> - **It is green against the fixed behaviour, not the old one.** The prompt's "tag it red or write it
>   green against current behaviour" applies only when the fix is not being made in the same change.
>
> Fix 1 is `PlannerTemplateApplier.IsCarvable`, consulted in `CarveTaskAroundBlockers` *and* in
> `MergeCarveExistingTasksAroundNew` **before** the removal is staged — returning the original from the
> carve alone would have handed `RemoveRange`/`AddRange` the same tracked instance and attempted an insert
> on an existing id. Fix 2 is the ordering rule on all three CRUD validators. **`MIG-3` — normalising the
> rows that predate the rule — is still outstanding and is being done separately.**

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

## The defect (`CQ-7`)

`AdhdTimeOrganizer.Planning/application/helper/PlannerTemplateApplier.cs`:

- `MergeCarveExistingTasksAroundNew` (:124-144) stages an overlapping existing task for **removal**,
  then replaces it with whatever `CarveTaskAroundBlockers` returns.
- `CarveTaskAroundBlockers` (:150-179) walks `currentStart` forward and only emits a segment when
  `currentStart < segmentEnd`. Every comparison assumes `StartTime < EndTime`.

For a task with `EndTime < StartTime` — an "overnight" row, which `POST /api/planner-task` accepts today
(the pre-existing `CQ-14`; see `PlannerTaskStatusAndSpanTests.
CreatingAnOvernightTask_IsAcceptedWithNegativeDuration`) — the walk runs past the end and returns an
**empty list**. The original is deleted, nothing is put back, its reminders cascade away, and the
endpoint answers **201**.

Trace for the concrete case below (existing 23:00→01:00, blocker 00:00→23:59):

```
currentStart = 23:00
blocker 00:00–23:59:  currentStart(23:00) < blocker.Start(00:00)?  no   -> emit nothing
                      currentStart = max(23:59, 23:00) = 23:59
tail:                 currentStart(23:59) < task.End(01:00)?       no   -> emit nothing
result: []
```

## Where

`AdhdTimeOrganizer.IntegrationTests/Endpoints/ApplyTemplatePlannerTaskConflictResolutionTests.cs` — the
one place all four modes are covered. Add a fifth test.

## Scenario

1. Seed an activity and a calendar day well outside the seeded years (e.g. `2029-03-05`).
2. Create an **overnight** planner task on it through the endpoint, so the test exercises the state the
   product can actually reach:

   ```csharp
   await CreateUserRoleClient().PostAsJsonAsync("api/planner-task", new
   {
       StartTime = new { Hours = 23, Minutes = 0 },
       EndTime   = new { Hours = 1,  Minutes = 0 },
       IsBackground = false,
       ActivityId = activityId,
       Status = "NotStarted",
       CalendarId = calendarId
   }, JsonOpts, CancellationToken);
   ```

   (This is accepted today — `PlannerTaskValidator` has no ordering rule. If `MIG-3` has already landed
   and the rule now exists, seed the row directly with `PlanningTestSeedHelper` instead and say so in a
   comment: the carving bug is independent of how the row got there.)
3. Attach a reminder to it with `ReminderSeedHelper`.
4. `POST /api/calendar/apply-planner-template` with `ConflictResolution = "MergeOverwrite"` and one
   template line spanning `00:00 → 23:59`.

## Assertions

- The response is 201 (it is; that is part of the problem).
- **The overnight task still exists**, or has been replaced by at least one segment covering part of its
  span. Today the day comes back with *only* the new template task and the original is gone.
- Its **reminder** is still registered — today `StagedTaskDeletions` faithfully cancels it, so the loss
  is internally consistent and completely silent.
- For contrast in the same test or a sibling: repeat with a normal 09:00→12:00 existing task and the
  same blocker, and assert it *is* correctly consumed. That is the behaviour you must not break while
  fixing this.

Expected to **fail** today. Tag it red or write it green against current behaviour with a
`// FINDING: CQ-7` comment naming the assertion to flip — see house rule 4 in the shared context.

## The fix this test drives

Two independent changes; the test should pass after either, and both are wanted:

1. Make `CarveTaskAroundBlockers` defensive — if it would return zero segments for a task the blockers
   did not fully cover, keep the original rather than dropping it.
2. Fix `CQ-14` upstream: add the `StartTime < EndTime` rule to `PlannerTaskValidator`,
   `RepeatingPlannerTaskValidator` and `TemplatePlannerTaskValidator`, matching
   `PlannerTaskChangeSpanValidator:19`. See `MIG-3` for the data migration and `DOC-1` for the doc line
   that currently claims all six already do this.
