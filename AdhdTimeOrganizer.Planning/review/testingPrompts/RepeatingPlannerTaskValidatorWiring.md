# TEST-11 — is `RepeatingPlannerTaskValidator` wired at all?

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

## The question this test answers (`CQ-8`)

`CreateRepeatingPlannerTaskEndpoint.cs` and `UpdateRepeatingPlannerTaskEndpoint.cs` are bare one-line
subclasses with no `Configure()` override:

```csharp
public class CreateRepeatingPlannerTaskEndpoint(DbContext dbContext)
    : BaseCreateEndpoint<RepeatingPlannerTask, RepeatingPlannerTaskRequest>(dbContext);
```

Every other endpoint in the slice that has a validator wires it explicitly —
`CreateTaskImportanceEndpoint`, `CreateTemplatePlannerTaskEndpoint`, `CreatePlannerTaskEndpoint`,
`CreateReminderEndpoint`, `UpdateCalendarEndpoint`. These two do not.

FastEndpoints **may** bind `Validator<RepeatingPlannerTaskRequest>` by convention from the assemblies in
`o.Assemblies`, in which case the 80 lines of `RepeatingPlannerTaskValidator` do run. Nothing in the
suite settles it: `PlanningCrudAuthMatrixTests` exercises only the Framework base behaviour, and no test
posts a malformed recurrence. So the slice's most intricate validator is either dead code or untested
code and the two are indistinguishable from outside.

**Write the test first. It tells you which.**

## Where

`AdhdTimeOrganizer.IntegrationTests/Endpoints/` — a new `RepeatingPlannerTaskValidationTests`.

## Request shape

```jsonc
POST api/repeating-planner-task
{
  "activityId": 1,
  "importanceId": 2,                 // NotNull AND GreaterThan(0) — required, unlike other task shapes
  "startTime": { "hours": 7, "minutes": 0 },
  "endTime":   { "hours": 8, "minutes": 0 },
  "isBackground": false,
  "recurrenceType": "DayOfWeek",     // DayOfWeek | DayOfMonth | DateRange | DayType
  "scheduledDays": ["Monday"],
  "scheduledDates": [],
  "activeFromDate": null,
  "activeToDate": null,
  "scheduledForDayTypes": [],
  "location": null,
  "notes": null
}
```

## Scenarios — one per rule in `application/validator/RepeatingPlannerTaskValidator.cs`

| # | Recurrence | Input | Expect 400 because |
|---|---|---|---|
| 1 | `DayOfWeek` | `scheduledDays: []` | must contain at least one day |
| 2 | `DayOfWeek` | `scheduledDays: ["Funday"]` | not a valid day name |
| 3 | `DayOfWeek` | `scheduledDays: ["monday"]` | `ValidDayNames` is exact-case — pin whichever answer the code gives |
| 4 | `DayOfMonth` | `scheduledDates: []` | must contain at least one value |
| 5 | `DayOfMonth` | `scheduledDates: [0]` / `[32]` | must be 1–31 |
| 6 | `DateRange` | `activeFromDate: null` | required for `DateRange` |
| 7 | `DateRange` | `activeToDate: null` | required for `DateRange` |
| 8 | `DateRange` | from **after** to | `ActiveFromDate` must be on or before `ActiveToDate` |
| 9 | `DayType` | `scheduledForDayTypes: []` | must contain at least one |
| 10 | `DayType` | `scheduledForDayTypes: ["Holiday"]` | **not a valid `DayType`** — the enum has five members and no `Holiday` (`DOC-2`). A holiday is a `Workday` row with a `HolidayName` |
| 11 | any | `importanceId: null` | `NotNull().GreaterThan(0)` — required here, optional on the other two task shapes |
| 12 | any | `activityId: 0` | positive |
| 13 | any | `location` 201 chars / `notes` 1001 chars | column limits |
| 14 | any | `startTime.hours: 24` | range |

Plus the controls: a well-formed request of **each** of the four recurrence types is `201`.

Run the whole table against `PUT /api/repeating-planner-task/{id}` too — `UpdateRepeatingPlannerTaskEndpoint`
has the same gap.

## Interpreting the result

- **If cases 1–14 return 400:** the validator is auto-bound and this is a coverage win, not a bug. Still
  add the explicit `Configure()` override to both endpoints for symmetry, so the slice does not depend on
  a convention the other eleven endpoints do not rely on. Downgrade `CQ-8` to 🟡 in the findings file.
- **If they return 201:** the validator is dead code. Add the override, re-run, and note the consequence
  in the test file: a `DayOfWeek` recurrence with `scheduledDays: []` is inert — the suggestion
  endpoint's `task.ScheduledDays.Contains(...)` at
  `GetSuggestionsRepeatingPlannerTaskEndpoint.cs:68` never matches, so the user's recurrence silently
  suggests nothing forever.

Either way, add case 15 from `PlannerTaskValidatorRules.md`: `endTime` before `startTime` is accepted
here too (`DOC-1`), and this validator is one of the three that lacks the ordering rule.
