# TEST-8 — the midnight-wrap time window, in both filter endpoints

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

## Why this one matters most among the read gaps

`FilterPlannerTaskEndpoint` (`application/endpoint/plannerTask/query/GetFilteredSortedPlannerTask.cs`)
is the **day-planner view's only read**. It carries eighteen lines of three-way overlap logic, and the
same eighteen lines are duplicated character-for-character in
`FilterTemplatePlannerTaskEndpoint` (`application/endpoint/templatePlannerTask/query/GetFilteredSortedTemplatePlannerTask.cs`)
over a different entity — `CQ-12`. Neither copy has a single test.

The predicate:

```csharp
var filterWrapsAround = until <= from;

query = query.Where(t => t.CalendarId == filter.CalendarId);   // TemplateId in the sibling

if (filterWrapsAround)
    query = query.Where(task =>
        (task.StartTime <= new TimeOnly(23, 59, 59) && task.EndTime >= from) ||
        (task.StartTime <= until && task.EndTime >= new TimeOnly(0, 0, 0)) ||
        task.EndTime < task.StartTime);
else
    query = query.Where(task =>
        (task.StartTime < until && task.EndTime > from) ||
        task.EndTime < task.StartTime);
```

Note the third clause in each branch: a task that itself wraps midnight is matched by **any** window.
That clause is the one that interacts with the overnight rows `CQ-14` lets in.

## Where

`AdhdTimeOrganizer.IntegrationTests/Endpoints/` — a new `PlannerTaskFilterWindowTests`. Write the cases
once as a `[Theory]` and run them against **both** endpoints, so the duplication is visible in the test
too and the eventual `TaskPlannerHelper.InTimeWindow` refactor has a single suite to keep green.

## Request

`POST /api/planner-task/filter` with a `PlannerTaskFilter` carrying `CalendarId`, `From`, `Until`
(both `TimeDto`). Sibling: `POST /api/template-planner-task/filter` with `TemplateId`.

## Fixture

One calendar day, one activity, and six tasks on it:

| # | Span | Notes |
|---|---|---|
| A | 00:00–01:00 | early edge |
| B | 08:00–09:00 | |
| C | 09:00–10:00 | |
| D | 12:00–13:00 | |
| E | 22:00–23:00 | late edge |
| F | 23:00–01:00 | **wraps midnight** — seed directly via `PlanningTestSeedHelper`, not through the endpoint |

## Cases

| Window | Wraps? | Expected |
|---|---|---|
| 08:00 → 14:00 | no | B, C, D, **F** |
| 09:00 → 10:00 | no | C, **F** — boundary: `StartTime < until && EndTime > from` is strict, so B and D are out |
| 09:30 → 09:45 | no | C (fully containing task), **F** |
| 22:00 → 02:00 | yes | A, E, **F** |
| 00:00 → 00:00 | yes (`until <= from`) | every task — this is the "whole day" window the home page used to send |
| 13:00 → 08:00 | yes | D, E, A, **F** |
| 03:00 → 04:00 | no | **F only** — the case that proves the third clause is doing something |

Also assert:

- `AlwaysSortBy` puts results in `StartTime` ascending regardless of a caller-supplied sort.
- A `CalendarId` belonging to **another user** returns an empty result, not that user's tasks — the
  endpoint relies entirely on the global query filter for this and nothing checks it.
- The sibling endpoint gives the same answers for the same windows over template lines.

## After the tests are green

Lift the predicate into `TaskPlannerHelper` as
`IQueryable<T> InTimeWindow<T>(this IQueryable<T> query, TimeOnly from, TimeOnly until) where T : BasePlannerTask`
and point both endpoints at it. The suite above is what makes that refactor safe, and it is the reason
to write these tests before touching the code.

`docs/domain-map.md` § *Navigation index* → the `TaskPlannerHelper` row gains the new extension.
