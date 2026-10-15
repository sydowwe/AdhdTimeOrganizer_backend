# P3-03 — Stopwatch check-in: "Still on X? 1 h 30 m"

> Paste into a fresh session. Read `review/notifications-opportunities/_SHARED-CONTEXT.md` first. Backlog #5.
> Framework (one kind) → History → frontend.

## Why

Hyperfocus and the forgotten stopwatch are the same failure seen from two sides. In the first the user loses the
afternoon; in the second History gets a 9-hour row the user then has to fix or delete. A pomodoro rings by itself; a
stopwatch never does, and the server doesn't even know one is running.

## Facts

- Client: `projectBoundaries` (`src/core/activityHistory/store/timerBoundaries.ts:82`, frontend repo
  `C:\Users\jakub\WebstormProjects\AntiProcrastinationApp-frontend`) returns `[]` for a stopwatch **on purpose**.
  `syncAlarms` (`store/runningTimerStore.ts:415`) posts whatever it returns as a whole-set replace keyed by
  `alarmSessionId`, and recomputes after every transition. Pause drops the set and resume re-posts it.
  `STALE_AFTER_MS` (12 h) drops a restored session. The API caps a set at 64 (`clampToContract`).
- Server (`AdhdTimeOrganizer.History`):
  - `ScheduleTimerAlarmsEndpoint` (`POST /activity-history/timer-alarm/schedule`, request
    `application/dto/request/timerAlarm/ScheduleTimerAlarmsRequest.cs`, `validator/ScheduleTimerAlarmsValidator.cs`).
  - `TimerBoundaryAlarmService`: one `RunOnceAt` Scheduler trigger per row, replace/cancel by session.
  - `TimerBoundaryAlarm` (no FK to the activity; `Tag` derived from the instant).
  - `TimerBoundaryAlarmJobHandler`: reads the row, builds `TimerBoundaryPayload`, notifies, deletes the row.
  - `TimerBoundaryAlarmRegistrar`: re-registers on boot.
- `TimerBoundary` is in `QuietHoursExemptTypes`, and that file's rule is "the user picked the instant". A check-in at
  +90 min is the app's choice, so **it has to be its own kind and must not be exempt.** Don't add a
  `TimerBoundaryPhase` member either: that would put a phase into `TimerBoundaryPayload` that the payload never
  carries.
- Quiet hours **defer**, and no kind can opt into "drop instead". A "still on X?" delivered at 07:00 about a stopwatch
  from 01:00 is noise, and so is its bell row.

## Design

1. Kind `StopwatchCheckIn`, payload `(string? ActivityName, int ElapsedMinutes, string? Tag, string? Url)`. The
   activity name follows the `TimerBoundaryPayload` precedent. Push on, email off, **not** held while away (the user
   started it), not quiet-hours exempt. No action buttons: Stop can't run server-side, because the stopwatch is
   client state ("This is not timer state and must never become it", `TimerBoundaryAlarm`). The tap opens `Url`, the
   stopwatch page, where Stop is one tap.
2. Reuse the alarm table, triggers and registrar. Add a History-owned discriminator on the row and the request
   (`TimerAlarmKind { Boundary, StopwatchCheckIn }`) plus `ElapsedMinutes int?`. The validator requires elapsed and
   no phase fields for a check-in. The job handler branches on the kind.
3. **Skip, don't defer:** at fire time, if the recipient is inside their window (`IQuietHoursReader` +
   `QuietHoursPolicy.IsWithin`), delete the row and send nothing. Log at debug.
4. Client cadence: the first at 90 min of *running* time, then every 60 min, up to 12 h (at most 11 rows). Only for a
   running, unpaused stopwatch. Carry the `Tag` so a live tab never shows a second copy.

## Tests (copy `TimerBoundaryAlarmTests`)

- Scheduling a check-in set and firing the handler writes a `StopwatchCheckIn` row with the elapsed minutes and the
  activity name.
- Inside quiet hours: no row, and the alarm row is gone.
- Cancel removes the set.
- The boot registrar re-registers check-ins.
- The validator rejects a check-in that carries phase fields.
- A boundary alarm still produces `TimerBoundary`.
- Catalog and vocabulary tests.
- Frontend: `projectBoundaries` returns check-ins for a running stopwatch and none when paused or ended
  (`runningTimerStore.test.ts`).

## Frontend

`store/timerBoundaries.ts`, `store/timerAlarmSchedule.ts`, `dto/request/ScheduleTimerAlarmsRequest.ts`,
`src/app/notifications/notificationTypeMeta.ts`.

## Open decisions

1. The cadence (90 + 60? user-configurable later?).
2. Bell row when skipped for quiet hours: recommended none.

## Resolved (2026-10-15). Implemented; this supersedes the design above where they differ

- **Cadence as proposed.** 90 minutes of running time, then hourly, up to 12 h (at most 11 rows), placed by running
  time: banked `accumulatedMs` plus the live segment, so check-ins an earlier segment passed are left out. Constants
  in the client's `timerBoundaries.ts`; not user-configurable yet.
- **No bell row when skipped.** The handler asks `IQuietHoursReader.GetQuietUntilAsync` at fire time (the P3-01
  reader answers "who is quiet now", so no `QuietHoursPolicy` call is needed) and never calls `NotifyAsync` for a
  recipient inside their window. A failed read sends it; the dispatcher still defers that push.
- **Phase is nullable on the row.** Migration `StopwatchCheckInAlarms` adds `kind` (default 0 = Boundary) and
  `elapsed_minutes`, and drops NOT NULL from `phase`. Its `Down` deletes pending check-ins first, or they would come
  back as phase 0 and ring "timer finished".
- **Check-ins are not a "running timer" for Planning's overrun nudge.** Not in the design above. `IRunningTimerLookup`
  counts `Boundary` rows only: a stopwatch has no planned end, so without this every stopwatch user would silently
  lose the "time's up" nudge. Pinned in `StopwatchCheckInAlarmTests`.
- **The tab does not ring a check-in itself.** The push reaches an open tab, and a local ring would ignore quiet
  hours. The tag (`stopwatch-check-in-<ms>`, its own prefix) only collapses two pushes for one check-in.
- **Frontend.** `projectBoundaries` returns the check-ins for a running stopwatch, `pushTimerAlarms` sends
  `kind: StopwatchCheckIn`, and `notificationTypeMeta` routes the type to the stopwatch page with an app-side label.
