# TEST-13 — the two suggestion endpoints against a stale materialized view

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

## Why

The three `PlannerSuggestionFrom*` entities are **keyless mappings over materialized views**
(`infrastructure/persistence/configuration/suggestion/`), refreshed by a **host-side** job. The view
therefore lags its base tables by design — that is the whole point of a materialized view — and both
endpoints dereference a principal navigation without checking it survived (`CQ-13`):

- `GetSuggestionsTaskPlannerDayTemplateEndpoint.cs:51,58` — `.Include(p => p.Template)` then
  `TaskPlannerDayTemplateResponse.Projection(new[] { p.Template }.AsQueryable()).Single()`.
- `GetSuggestionsRepeatingPlannerTaskEndpoint.cs:158,183` — `p.Activity.Role.Color` and
  `MapActivity(p.Activity)`.

Separately, `GetSuggestionsTaskPlannerDayTemplateEndpoint` has **no test whatsoever** — a standing known
gap in `docs/testing.md`, and the reason a broken projection there is silent.

## Where

- Stale-view cases: `AdhdTimeOrganizer.IntegrationTests/Endpoints/RepeatingPlannerTaskSuggestionsTests.cs`,
  which already has `RefreshSuggestionViewsAsync()` and the seeding you need — extend it.
- The day-template endpoint's happy path: a new `TaskPlannerDayTemplateSuggestionsTests` in the same
  folder, modelled on the repeating-task one.

## Part 1 — the stale-view 500s (`CQ-13`)

Both are **expected to fail** today with an unhandled `NullReferenceException` surfacing as a 500.

### A — deleted day template

1. Seed a day template and enough applications of it on the same weekday to make
   `mv_template_suggestion_pattern` produce a row (mirror the three-occurrence threshold
   `RepeatingPlannerTaskSuggestionsTests` establishes for its own view).
2. `RefreshSuggestionViewsAsync()`.
3. **Delete the template** — through `DELETE /api/task-planner-day-template/{id}`, so the scenario is one
   a user genuinely produces.
4. Do **not** refresh again. `GET /api/task-planner-day-template/suggestions/{calendarId}`.

Assert: **200**, and the suggestion for the deleted template is simply absent. Today `p.Template` is
null and the projection throws — a 500 on a page load, caused by an action the user just took.

### B — deleted activity

Same shape against `GET /api/repeating-planner-task/suggestions/{calendarId}`: seed a pattern into
`mv_planner_task_pattern`, refresh, delete the `Activity`, do not refresh, call.

Assert 200 with the row absent. Watch out for the delete graph here — `PlannerDeleteBehaviorTests.
DeletingActivityReferencedByTask_Cascades_NotRestricted` shows an activity delete cascades its planner
tasks away, so this scenario also exercises the "view row outlives both" case.

Do the same for `mv_activity_history_pattern`, whose rows are the third tier in the same endpoint.

### The fix these drive

`.Where(p => p.Template != null)` / `.Where(p => p.Activity != null)` before projecting, or an inner
join through the navigation. Then `docs/domain-map.md` § *Suggestion read-models* should say that a view
row may outlive its principal until the next refresh and that the endpoints tolerate it.

## Part 2 — `GetSuggestionsTaskPlannerDayTemplateEndpoint`'s happy path

Nothing covers this endpoint. Its logic:

```csharp
.Where(p => p.UserId == userId &&
            ((p.PatternType == 0 && p.PatternValue == isoDayOfWeek) ||
             (p.PatternType == 1 && p.PatternValue == (int)calendar.DayType)))
```

then ordered by `OccurrenceCount` descending, with

```csharp
PatternLabel = p.PatternType == 0 ? DayNames[p.PatternValue - 1] : ((DayType)p.PatternValue).ToString()
```

Cases:

- **Day-of-week pattern** (`PatternType == 0`): a template applied on N Tuesdays suggests itself for a
  Tuesday and not for a Wednesday. `PatternLabel` reads `"Tuesday"` — note `DayNames` is Monday-indexed
  and the code subtracts 1, so `isoDayOfWeek` must be 1..7. Assert Sunday specifically: `date.DayOfWeek`
  is 0 for Sunday and the endpoint maps it to 7 (`:45`), so Sunday is the case where an off-by-one
  becomes an `IndexOutOfRangeException`.
- **Day-type pattern** (`PatternType == 1`): matches on the calendar's `DayType`, and `PatternLabel` is
  the enum name. `DayType` has **five** members and no `Holiday` (see `DOC-2`) — a holiday is a
  `Workday` row with a `HolidayName`.
- **Ordering**: higher `OccurrenceCount` first.
- **Cross-user**: another user's patterns never appear, and another user's `calendarId` is a 404.
- **Empty**: a calendar day with no matching pattern is `200` with `[]`, never a 404.
