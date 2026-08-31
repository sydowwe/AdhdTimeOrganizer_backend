# TEST-5 — `PlannerTaskValidator`'s rules

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first. See `ForeignKeyOwnership.md` for the security
half of this endpoint's input validation.

## Why

`PlannerTaskValidator` (`application/validator/PlannerTaskValidator.cs`) guards **both**
`POST /api/planner-task` and `PUT /api/planner-task/{id}` — the main write path for the slice's central
entity. It has ten rules. Exactly one is covered today
(`LazyCalendarCreationTests.ATaskNamingItsDayTwice_OrNotAtAll_IsRejected`).

## Where

`AdhdTimeOrganizer.IntegrationTests/Endpoints/` — a new `PlannerTaskValidationTests`.

## Request shape

```jsonc
POST api/planner-task
{
  "startTime":  { "hours": 9, "minutes": 0 },   // TimeDto
  "endTime":    { "hours": 10, "minutes": 0 },
  "isBackground": false,
  "activityId": 1,
  "importanceId": null,
  "status": "NotStarted",                        // enum as string
  "calendarId": 1,                               // XOR with "date"
  "date": null,                                  // "2029-03-05"
  "todolistId": null,
  "location": null,
  "notes": null,
  "actualStartTime": null                        // TimeDto, create-only
}
```
Response: `201` with the new id as a bare number.

## Scenarios — each is one `[Theory]` case or one `[Fact]`

| # | Input | Expect | Status today |
|---|---|---|---|
| 1 | `startTime.hours = 24` | 400 | passes |
| 2 | `startTime.minutes = 60` | 400 | passes |
| 3 | `endTime.hours = -1` | 400 | passes |
| 4 | both `calendarId` and `date` set | 400 | **covered** by `LazyCalendarCreationTests` — assert it here too, cheaply |
| 5 | neither set | 400 | as above |
| 6 | `calendarId = 0` | 400 | passes |
| 7 | `activityId = 0` | 400 | passes |
| 8 | `importanceId = 0` (present but zero) | 400 | passes |
| 9 | `todolistId = 0` | 400 | passes |
| 10 | `location` 201 chars | 400 (column is 200) | passes |
| 11 | `notes` 1001 chars | 400 (column is 1000) | passes |
| 12 | `actualStartTime` set with `status = "NotStarted"` | 400 | passes |
| 13 | `actualStartTime` set with `status = "Cancelled"` | 400 | passes |
| 14 | `actualStartTime` set with `status = "InProgress"` | 201 | passes — the control for 12/13 |
| 15 | **`endTime` earlier than `startTime` (23:00 → 01:00)** | **400** | **fails** — see below |
| 16 | `endTime` equal to `startTime` | **400** | **fails** — same reason |

### 15 and 16 are the point of this prompt

`PlannerTaskValidator` has **no ordering rule**. `PlannerTaskChangeSpanValidator:19` has one
(`StartTime.ToTimeOnly() < EndTime.ToTimeOnly()`), and so do the two apply-template validators; the
three CRUD validators (`PlannerTaskValidator`, `RepeatingPlannerTaskValidator`,
`TemplatePlannerTaskValidator`) do not.

This is the pre-existing `CQ-14` and is already pinned green-against-current-behaviour by
`PlannerTaskStatusAndSpanTests.CreatingAnOvernightTask_IsAcceptedWithNegativeDuration`, which asserts
`Created` and names the assertion to flip. **Do not duplicate that test.** Instead:

- Add cases 15/16 for the **`PUT`** path, which that probe does not cover.
- Add the same two cases for `POST /api/repeating-planner-task` and
  `POST /api/template-planner-task`, which nothing covers at all.
- Note in a comment that `docs/domain-map.md:91` currently claims *every* planner-task validator
  rejects this (`DOC-1`), which is why the gap is easy to miss.

Follow the existing probe's convention: green against current behaviour, with the assertion to flip
named in a comment. Case 15 for `POST /api/planner-task` is already that test — cross-reference it
rather than writing a second copy.

## Also worth one test each

- **`UpdateEntity` leaves `ActualStartTime` alone.** `PlannerTaskRequest:44-48` documents this
  deliberately: a full `PUT` that omits the field must not wipe the start time of a task already under
  way. Seed a task with `ActualStartTime = 09:05`, `PUT` without the field, assert it survives. Nothing
  covers this and it is exactly the kind of thing a mapping refactor breaks.
- **`UpdateEntity` clears `TodolistItemId` when the field is omitted** (`:82`) — the opposite
  convention on the same DTO. Whether that is intended is worth settling; pin whichever answer is
  chosen.
