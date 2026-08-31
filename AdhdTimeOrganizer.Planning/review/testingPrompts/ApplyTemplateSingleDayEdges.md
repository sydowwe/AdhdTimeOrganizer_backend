# TEST-7 — `ApplyTemplatePlannerTaskEndpoint` edges and error mapping

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

## Why

`POST /api/calendar/apply-planner-template`
(`application/endpoint/plannerTask/command/ApplyTemplatePlannerTaskEndpoint.cs`) is well covered for the
**four conflict modes** (`ApplyTemplatePlannerTaskConflictResolutionTests`) and for lazy day creation
(`LazyCalendarCreationTests.ApplyingATemplateByDate_CreatesTheDay`). Its edges are not covered at all:
no cross-user case, no error mapping, no `by-id-404` case.

## Where

Extend `AdhdTimeOrganizer.IntegrationTests/Endpoints/ApplyTemplatePlannerTaskConflictResolutionTests.cs`,
or a new `ApplyTemplateEdgeTests` beside it.

## Request

```jsonc
POST api/calendar/apply-planner-template
{
  "templateId": 1,
  "calendarId": 1,               // XOR with "date"
  "date": null,                  // "2029-03-05"
  "conflictResolution": "Ignore",   // Ignore | Overwrite | MergeIgnore | MergeOverwrite
  "tasksFromTemplate": [ { …PlannerTaskRequest… } ]
}
```
Response: **201** with `{ calendar, tasks }`.

Note `tasksFromTemplate` comes from the **client**, not from the template's own lines — the endpoint
stamps its resolved `calendarId` onto each. `ApplyTemplateToTaskPlannerValidator` deliberately has no
rule on the nested `calendarId`/`date` for that reason.

## Scenarios

### A — by id vs by date, on an unplanned day

`domain-map.md` § *Lazy day creation* states the asymmetry: **by date** creates the day; **by id** still
404s, *"because an id resolving to nothing is a stale client rather than an unplanned day."*

- `date` = a date outside the seeded years → 201, and the calendar row now exists.
  (Covered by `LazyCalendarCreationTests`; assert cheaply here for contrast.)
- `calendarId` = a nonexistent id → **404**, and no calendar row is created.

### B — cross-user calendar

As user A, send user **B's** `calendarId`. The global filter means it does not resolve, so expect
**404**. Assert B's day is untouched — no tasks added, `AppliedTemplateId` still null, and B's template
`UsageCount` unchanged.

Contrast with the batched sibling, which answers `succeeded: false` for the same situation
(`CalendarBatchEndpointTests.ApplyTemplateBatch_AppliesPerDay_AndFailsOnlyTheUnreachableDay`). The two
shapes are deliberate; pinning both together makes that legible.

### C — cross-user template

As user A, own calendar, user **B's** `templateId` → **404**, nothing written, B's template
`UsageCount` and `LastUsedAt` unchanged.

### D — the stamp

On success, assert all five stamp effects (`PlannerTemplateApplier.StampTemplate:93-102`):
`Calendar.AppliedTemplateId`, `Calendar.AppliedTemplateName`, `WakeUpTime`/`BedTime` taken from the
template **when it has them** — and *left alone* when it does not (`?? calendar.WakeUpTime`) — plus
`Template.UsageCount + 1` and `LastUsedAt` set.

`AssertTemplateStampAsync` already exists in the conflict-resolution tests; reuse it and add the
null-defaults case, which it does not cover.

### E — `CQ-9`: error mapping (**expected to fail today**)

`:92-97` computes `DbUtils.HandleException(ex, "Create").ErrorType` and then hardcodes
`Send.ErrorsAsync(400, ct)`, discarding it. `PatchPlannerTaskStatusEndpoint.cs:85-87` does the same call
correctly with `EndpointHelper.ToStatusCode(result.ErrorType)`.

Provoke a concurrency conflict on the calendar's `row_version` — two applies to the same day, or load
the calendar in a second context, bump it, then apply. Assert **409**, not 400.

Today this returns 400. Write it red-and-tagged or green-with-`// FINDING: CQ-9`. Note the `Summary`
block does not declare 409 either, so the OpenAPI contract currently matches the bug; the fix should add
`s.Response(409, …)`.

### F — validator rules

`ApplyTemplateToTaskPlannerValidator`:

- both `calendarId` and `date` → 400; neither → 400 (the same XOR as `PlannerTaskRequest`).
- `tasksFromTemplate` null or empty → 400.
- a nested task with `startTime >= endTime` → **400**. This validator *does* have the ordering rule, unlike
  `PlannerTaskValidator` — worth pinning as the reference behaviour the other three should match
  (`DOC-1`).
- a nested task with `activityId = 0` → 400.
- `conflictResolution` outside the enum → 400.

### G — reminders on overwritten tasks

`Overwrite` and both merge modes delete existing tasks, cascading their reminders away.
`StagedTaskDeletions` reads the ChangeTracker **before** the save so the module-side definitions can be
cancelled after it. The conflict-resolution tests already assert this via `AssertReminderCancelledAsync`
— add the negative control they lack: with `ConflictResolution = "Ignore"`, **no** existing task is
deleted, so an attached reminder must **still be registered** afterwards.
