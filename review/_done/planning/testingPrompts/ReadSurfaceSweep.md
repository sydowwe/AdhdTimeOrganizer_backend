# TEST-1 · TEST-2 · TEST-9 · TEST-12 · TEST-14 · TEST-15 · TEST-20 — the untested read surface

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

One prompt covering seven endpoints and one seam that have **no test at all**. Each is small; together
they are the largest single block of the slice's coverage gap. Write them as one
`PlanningReadSurfaceTests` class in `AdhdTimeOrganizer.IntegrationTests/Endpoints/`, one region each.

The shape is the same every time and is what makes them worth writing at all:

1. Seed rows for **two** users.
2. Call as user A.
3. Assert the response contains A's rows, **and specifically not B's** — not just "the right count".
4. Assert an anonymous caller gets 401.

That is the assertion set a lost or mis-scoped query filter fails, and none of these endpoints has it.

---

## TEST-1 — `GetByDateCalendarEndpoint`

`GET /api/calendar/by-Date/{value}` — `BaseGetByFieldEndpoint`, `application/endpoint/calendar/GetByDateCalendarEndpoint.cs`.

- Format: `dd-MM-yyyy` **only** (unlike day-plan, which takes both — see below). `"2029-03-05"` must be
  a 400 with the message *"Date must be in dd-MM-yyyy format."*; `"05-03-2029"` is the valid form.
- A date with no calendar row is a **404** — deliberately, unlike day-plan. Pin both halves in one test;
  the pair is a documented product decision (`domain-map.md` § *The one-request day plan*).
- The response carries `Streak` **non-null**. `PlannerStreakTests.
  Streak_OfThreeCompleteDays_RidesAlongOnThePlanResponse` covers this; do not duplicate it, but do assert
  the 404 and format cases, which nothing covers.
- Cross-user: B's date resolves to 404 for A.

## TEST-2 — `FilterCalendarEndpoint`

`POST /api/calendar/filter` — `BaseFilterEndpoint`, `application/endpoint/calendar/GetFilteredSortedCalendar.cs`.
`CalendarFilter` carries `From` / `Until` (`DateOnly`).

- The inclusive range is honoured at both edges (a row exactly on `From` and one exactly on `Until` are
  both returned; `From - 1` is not).
- `AlwaysSortBy` puts `Date` ascending regardless of what the caller asks.
- `TotalTasks` / `CompletedTasks` are correct — `CalendarResponse.Projection:62-63` computes them as two
  correlated subqueries, and nothing checks the `CompletedTasks` predicate is `Status == Completed`
  rather than, say, `IsDone`.
- Cross-user: B's days never appear.
- **`PERF-2`:** unlike `calendar/task-summaries` and `planner-task/plan-vs-actual-trend`, this endpoint
  applies **no range cap**. Send `From = 1900-01-01, Until = 2100-01-01` and assert whatever is decided —
  either a 400 (after a `CalendarFilter` validator is added, matching `CalendarTaskSummariesValidator`)
  or, for now, a green pin with a `// FINDING: PERF-2` comment recording that it is accepted.

## TEST-9 — `FormSelectOptionsPlannerTaskEndpoint`

`application/endpoint/plannerTask/query/FormSelectOptionsPlannerTaskEndpoint.cs`. Route comes from
`BaseActivityFormSelectOptionsEndpoint` with `EntityRoute => "planner-task"`.

The base query is `Set<PlannerTask>().FilteredByUser(userId).Select(pt => pt.Activity).Distinct()`, so
it returns activities **the caller has actually planned**, not every activity they own. Assert exactly
that: seed A with three activities but planner tasks for only two, and assert the third is absent. Then
seed B a planner task and assert B's activity does not appear for A.

## TEST-12 — `GetAllRepeatingPlannerTaskEndpoint`

`BaseGetAllEndpoint`, no overrides. Two users, assert scoping, assert an anonymous 401. Ten lines.

## TEST-14 — `TemplatePlannerTaskChangeSpanEndpoint`

`PATCH /api/template-planner-task/{id}` — `BasePatchEndpoint`,
`application/endpoint/templatePlannerTask/command/TemplatePlannerTaskChangeSpanEndpoint.cs`.

- Happy: the span moves, and **nothing else on the row changes** (re-read and compare the other columns).
- `PlannerTaskChangeSpanValidator` rejects `EndTime <= StartTime` → 400. This is the *only* one of the
  three span-ish validators that has the rule, so it is worth pinning here as the reference behaviour
  the others should match (`DOC-1`).
- Cross-user: B's template line is a 404, and stays unchanged.

## TEST-15 — `GridTaskImportanceEndpoint` and the two `GetSelectOptions` routes

- **Grid** (`application/endpoint/taskImportance/query/GridTaskImportanceEndpoint.cs`): the four filters
  in `ApplyCustomFiltering` — `Text` contains, `Color` exact, `MinImportance`, `MaxImportance` — plus
  cross-user scoping. Note `Text.Contains` is **case-sensitive** in Postgres; that is a known portal-wide
  family (Core review's `GridFilterCaseSensitivity.md`), so pin current behaviour with a comment rather
  than treating it as a new finding.
- **`GetSelectOptionsTaskImportanceEndpoint`**: maps to `SelectOptionResponse(t.Id, t.Text)`; assert
  scoping and that the two seeded sentinels (Critical 999, Optional 666) are present for a fresh user.
- **`GetSelectOptionsTaskPlannerDayTemplateEndpoint`**: currently reached only by
  `PlanningRouteSmokeTests`, which asserts `NotBe(NotFound)`. Assert the payload and the scoping.

## TEST-20 — `CalendarDayLookup` (Core seam)

`application/seam/CalendarDayLookup.cs` — resolved **by type** as `ICalendarDayLookup`, and read by
History's calendar dashboard so History need not reference Planning. Covered today only indirectly,
through History's own tests.

Test it directly: resolve `ICalendarDayLookup` from the test host, call
`GetDaysAsync(db, userIdA, from, until, ct)` with days seeded for both users, and assert:

- Only A's days come back, and the range is inclusive at both ends.
- `DayIndex` is **ISO** — Monday 1 … Sunday **7**, not `DayOfWeek`'s Sunday-0. The property is computed
  in C# (`Calendar.cs:35`) and projected in memory precisely because it cannot go to SQL; a refactor that
  pushes the projection into the query silently changes Sunday from 7 to 0 and History's dashboard
  buckets shift by a day with nothing failing.
- `HolidayName`, `Label`, `WakeUpTime`, `BedTime` and `DayType` all survive the projection.
