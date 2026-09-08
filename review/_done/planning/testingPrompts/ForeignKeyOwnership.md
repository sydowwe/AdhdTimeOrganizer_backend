# TEST-5 (part) / `SEC-1` — foreign-key ids must belong to the caller

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

> Companion to `PlannerTaskValidatorRules.md`. This one is the security half.

## The defect (`SEC-1`)

`PlannerTaskRequest` (`application/dto/request/taskPlanner/PlannerTaskRequest.cs:52-83`) writes
`ActivityId`, `ImportanceId` and `TodolistId` straight onto the entity. `PlannerTaskValidator` checks
only that each is `> 0`. The database FK proves the row **exists**; nothing proves it is the caller's.
Same for `UserPlannerSettingsRequest.DefaultApplyTemplateId`, and for the `ActivityId`s in the task list
`PlannerTemplateApplier.BuildTasks` builds from `ApplyTemplateToTaskPlannerRequest`.

The correct pattern already exists two folders over —
`CreateReminderEndpoint.AfterMapping:27-37` probes the user-scoped set with `AnyAsync`, which makes
"does it exist" and "is it mine" one question, and `ReminderRegistrationTests.
AttachingToAnotherUsersTask_IsRefused` pins it.

This is the same family as the Core review's `CoreForeignKeyOwnership.md` and the TodoLists review's
`CreateTodoListItemForeignKeyOwnership.md` — a third instance, in a third slice.

## Where

`AdhdTimeOrganizer.IntegrationTests/Endpoints/` — a new `PlannerForeignKeyOwnershipTests`.

## Scenarios

All of these are **expected to fail** today. Each should be a 400 or 404, and each currently succeeds.

### A — another user's `Activity`

Seed activity `X` owned by **user B**. As **user A**, `POST /api/planner-task` with
`ActivityId = X`, `CalendarId` = one of A's own days.

Assert: 400 or 404, and no row in `planner_task` references `X`.

Then the leak that makes this more than an integrity nit — **assert it before you fix it**, so the
severity is on record:

```csharp
// Today this returns 201. Read the day back as user A:
var day = await clientA.GetFromJsonAsync<DayPlanResponse>($"api/calendar/day-plan/{date:yyyy-MM-dd}", …);
// day.Tasks[0].Activity.Name and .Activity.Role.Color are user B's.
```

`PlannerTask.Color => Activity.Role.Color` and `PlannerTaskResponse` both project through that
navigation, so B's activity name, role name and role colour are served to A.

### B — another user's `TaskImportance`

Same shape with `ImportanceId`. Extra consequence worth asserting: `BasePlannerTask.IsOptional` compares
`Importance.Importance` against `666`, so pointing at another user's importance row lets A's task's
optional-ness be decided by B's data — which changes A's **streak** denominator
(`PlannerStreakReader.cs:44`).

### C — another user's `TodoListItem`

Same shape with `TodolistId`. Then fire the to-do toggle **as user B** and assert A's task is
untouched.

That assertion passes today, but only by accident: `TodoListItemIsDoneChangedEventHandler.cs:36-39`
has no `UserId` predicate and is saved by the global query filter binding to B's HTTP scope. Pin it, and
pin it again after `SEC-2` adds the explicit predicate — at which point it passes on purpose.

### D — another user's `TaskPlannerDayTemplate` as the settings default

As user A, `PUT /api/planner/settings` with `DefaultApplyTemplateId` = one of B's templates.

Assert: 400 or 404. Note this one leaks nothing today — `UserPlannerSettingsResponse` returns the bare
id and no name — so it is an integrity defect only. Say so in a comment so nobody upgrades the severity
later without re-checking the response shape.

### E — the template-apply path

`POST /api/calendar/apply-planner-template` with `TasksFromTemplate[0].ActivityId` = one of B's
activities. Same assertion as A. This path does **not** go through `PlannerTaskValidator` at all — it
uses `ApplyTemplateToTaskPlannerValidator`, which likewise only checks `> 0` — so fixing the CRUD
endpoints alone leaves this one open.

### F — the control

The same requests with the caller's **own** ids must still succeed. Without this, a fix that rejects
everything passes A–E.

## The fix this test drives

An ownership probe in `AfterMapping` on `CreatePlannerTaskEndpoint`, `UpdatePlannerTaskEndpoint`,
`UpdatePlannerSettingsEndpoint`, and over `BuildTasks`' input in both apply-template endpoints —
`AnyAsync` through the filtered set, exactly as `CreateReminderEndpoint` does it.

`docs/domain-map.md` § *Invariants* should gain a row for this once it holds.
