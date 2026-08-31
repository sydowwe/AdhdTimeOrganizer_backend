# TEST-3 — the two `PlannerBatchLimits` ceilings, at their boundary

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

## Why

`PlannerBatchLimits` (`application/helper/PlannerBatchLimits.cs`) holds `MaxCalendarIds = 366` and
`MaxSummaryRangeDays = 366`. They exist because the batched routes are the only planner endpoints whose
cost scales with something the client picks, and batching removed the browser connection limit that used
to bound them accidentally.

Existing tests exercise **inverted** and **duplicate** inputs
(`CalendarBatchEndpointTests.TaskSummaries_RejectsInvertedRange`,
`ApplyTemplateBatch_RejectsDuplicateCalendarIds`, `PlanVsActualTrendTests.Trend_WithUntilBeforeFrom_IsRejected`)
but never an over-length one. `docs/testing.md` lists this as a known gap: *"raising a ceiling by
accident goes unnoticed."*

## Where

`AdhdTimeOrganizer.IntegrationTests/Endpoints/CalendarBatchEndpointTests.cs` (id-list routes) and
`PlanVsActualTrendTests.cs` (the range route).

## Part 1 — `MaxCalendarIds`, via `CalendarIdListRules`

`application/validator/CalendarBatchValidators.cs:92-105` applies the same rule set to four requests:
non-null, non-empty, all positive, all distinct, `Count <= MaxCalendarIds`.

Routes and their id property:

| Route | Property |
|---|---|
| `POST /api/calendar/apply-planner-template/batch` | `CalendarIds` |
| `POST /api/planner-task/copy-to-days` | `TargetCalendarIds` |
| `PATCH /api/calendar/day-type/batch` | `CalendarIds` |

For each, as a `[Theory]` over the three routes:

- **366 ids** → accepted (the ids need not exist; they come back `succeeded: false`, which is the point —
  the ceiling is a validation rule, not a data one).
- **367 ids** → **400**, with a message naming the limit.
- **empty list** → 400.
- **null list** → 400.
- **one id ≤ 0** → 400.

Generating 367 distinct non-existent ids is fine — use `Enumerable.Range(900_000, 367).Select(i => (long)i)`.
Assert the 366 case really did reach the handler by checking `results` has 366 entries, in request order.

## Part 2 — `MaxSummaryRangeDays`

Two routes share the ceiling and, per `PlanVsActualTrendValidator`'s own doc comment, the same two rules:

| Route | Request |
|---|---|
| `POST /api/calendar/task-summaries` | `CalendarTaskSummariesRequest { From, Until }` |
| `POST /api/planner-task/plan-vs-actual-trend` | `PlanVsActualTrendRequest { From, Until }` |

The rule is `Until.DayNumber - From.DayNumber + 1 <= 366`, i.e. **inclusive**. So:

- `From = 2029-01-01`, `Until = 2030-01-01` → 366 days → accepted.
- `From = 2029-01-01`, `Until = 2030-01-02` → 367 days → **400**.
- `From == Until` → 1 day → accepted.
- `Until` before `From` → 400 (already covered; assert here too so the boundary and the ordering rules
  live together).

## Part 3 — the one that has no ceiling

`POST /api/calendar/filter` (`FilterCalendarEndpoint`) takes the **same** `From`/`Until` shape and applies
**no cap at all** — `PERF-2`. Add a case sending a 200-year range and pin whatever is decided: a 400
once a `CalendarFilter` validator is added, or a green pin with a `// FINDING: PERF-2` comment for now.

Keeping all three range routes in one test file is deliberate — it makes the odd one out visible.
