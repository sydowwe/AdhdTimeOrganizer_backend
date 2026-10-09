# P1-04 — Missed and overrun blocks (audit N5)

> Paste into a fresh session. Read `review/notifications-opportunities/_SHARED-CONTEXT.md` first.

Two time-blindness nudges, one sweep:

- **Missed** — a block's time came and went and it never started. "2 blocks slipped — re-plan the rest of today?"
- **Overrun** — a block is still running past its end. "Time's up for X — wrap up or extend?"

## Facts (verified 2026-10-09)

- `AdhdTimeOrganizer.Planning/domain/service/PlannerTaskMissedRule.cs` — the **one** definition of missed (and late /
  upcoming): `IsMissed(task, date, userNow)`, `Classify(…)` → `PlannerTaskSlipState?`, `EndsAt(date, start, end)`.
  Its own doc: *"audit N5's 'you missed …' nudge"* should read it, not re-derive it. Missed = `NotStarted`, foreground,
  end passed on the user's clock.
- `CompleteFinishedPlannerTasksJobHandler` — every 5 min (`PlanningScheduledJobsRegistrar.cs:26`), unauthenticated,
  zones via `IUserTimeZoneReader`, `TimeProvider` injected. It closes `AutoCompleteAtEnd` tasks that are
  `InProgress` past their end — the overrun sweep is its sibling for every other task.
- FO4: `POST /calendar/day-plan/{date}/replan` (preview), `…/replan/apply`, `GET …/next-free-slot`.

## Design

### One handler, two signals

New `PlannerSlipNudgeJobHandler` in `AdhdTimeOrganizer.Planning/infrastructure/jobs/`, 5- or 15-min cron, registered in
`PlanningScheduledJobsRegistrar`, key added to `ModuleWiringTests`. Separate from `CompleteFinishedPlannerTasksJobHandler`
(that one writes status; this one only notifies).

Query: tasks whose calendar date is in {UTC yesterday, today, tomorrow} (covers every zone's today), status
`NotStarted` or `InProgress`, not yet notified — then apply the rule per user in memory against their local now.
Consider a partial index if the plan shows a scan.

Producer: a Planning `IPlannerNotificationService`, the way Routines has one (`_SHARED-CONTEXT.md` § *Producing a
notification*).

### Missed

- Kind `PlannedBlocksMissed`, payload `(DateOnly Date, int Count, long? FirstTaskId = null)` — one notification
  **per user per sweep**, not per block.
- Mark per task: `PlannerTask.MissedNotifiedAt` (migration) so each block is announced once.
- **Cap**: e.g. at most 2 missed pushes per user per day; after that, stop (or bell only once
  [P2-02](P2-02-per-kind-push-defaults.md) exists). A bad day must not become a stream of reproaches.
- Deep link to the FO4 preview for that date.
- Held while away: **yes**. Quiet hours: automatic.
- **Decision:** fire on *missed* (end passed) or on *late* (start + 10 min, still time to act)? Recommendation: missed
  first. Late overlaps with [P1-03](P1-03-planner-block-reminders.md)'s start reminder — if late is added later, only for
  blocks whose start reminder was not delivered.

### Overrun

- `InProgress`, **not** `AutoCompleteAtEnd`, `EndsAt` passed by ≥ 5 min.
- Kind `PlannedBlockOverrun`, payload `(long PlannerTaskId, string? ActivityName = null, int MinutesOver = 0)` — the
  activity name is the user's own, same precedent as `TimerBoundaryPayload`.
- Mark `PlannerTask.OverrunNotifiedAt`; once per task.
- Actions the deep link (and later [P2-03](P2-03-push-action-buttons.md)) offers: done (`PatchPlannerTaskStatusEndpoint`),
  extend 15 min (`PatchPlannerTaskSpanEndpoint`).
- Held while away: yes (unsolicited, about a moment). Only today's tasks, so a block left `InProgress` days ago never
  pings.
- ⚠ **Timer overlap:** if the user runs a Pomodoro for that block, History's `TimerBoundary` alarms already ring.
  Planning cannot see History's `TimerBoundaryAlarm` table. Options: accept it, or a small Core seam ("does this user
  have a live timer alarm?") implemented by History. Decision.

## Tests

Fixed `TimeProvider`, two users in different zones (model: `DayReplanTests`):

- a missed block → one notification row with the right count; the mark is set; the next run sends nothing;
- three missed blocks in one run → **one** notification, `Count = 3`;
- the cap holds across runs;
- an overrunning `AutoCompleteAtEnd` task → nothing (the other job owns it);
- an away user → bell row, no push (`NotificationAwayHoldTests` pattern).

## Open decisions

1. Missed vs late trigger.
2. The daily cap.
3. The timer-overlap seam.
