# P3 — Backlog

> Read `review/notifications-opportunities/_SHARED-CONTEXT.md` first. Each idea here is a sketch, not a prompt — promote
> one to its own `P*.md` file when you pick it up. Most bell-only ideas need [P2-02](P2-02-per-kind-push-defaults.md).

| # | Idea | Slice | Channel | Mechanism | Needs |
|---|---|---|---|---|---|
| 1 | **Evening shutdown** at bed time − 60 min: daily recap, plan vs actual, "tomorrow has no plan", carry-over | host (reads Planning, TodoLists) | Push, opt-in, held while away | zone-aware sweep like the briefing | FO10 (bed time is 00:00 until then) |
| 2 | **Weekly review** (Sunday ~18:00 local): routines (the weekly review card in `UserRoutineSettings` exists), plan-vs-actual trend, skip summary, to-dos finished, tracked time from the unified dashboards | host | **Email, opt-in** + bell | weekly zone-aware sweep | P2-02 if push should be off |
| 3 | **Streak milestone** — a new best, or 7 / 30 / 100 | Routines | inside `RoutinePeriodEnded` text | add `IsNewBest` to its payload | — |
| 4 | **Time-of-day routine items** ("meds at 8:00") that stay silent once ticked | Routines | Push | reminder linked to a `RoutineTodoList` item; resolver returns nobody once done in the current period (P1-02's shape) | a time field on the routine item |
| 5 | **Stopwatch check-in** — "Still on X? 1 h 30 m" (hyperfocus, forgotten stopwatch). The stopwatch is not a session, so the server never knows it runs | History | Push | client schedules it through the existing `/activity-history/timer-alarm/` endpoints; new `TimerBoundaryPhase` value | decide whether it is quiet-hours-exempt like `TimerBoundary` |
| 6 | **Mapping suggestions ready** (FO9) when ≥ N would be offered | Tracking | Bell only, weekly | sweep over `MappingSuggestionRule` | P2-02 |
| 7 | **Stuck to-do** — FO3's `TodoListItemPlanHelp` says stuck: "split it into steps?" | TodoLists | Bell only, weekly | sweep | P2-02 |
| 8 | **Weekend weather window** (Friday ~17:00 local): N backlog / bucket-list items fit the forecast | ActivityProfiles | Push, opt-in | `WeatherFitRule` + `OpenMeteoWeatherProvider` (the only outbound HTTP) — cache per `WeatherLocation` | rate limits |
| 9 | **Memory anchors** — "N years ago this month" | ActivityProfiles | Bell only, monthly | `MemoryAnchor.AnchorMonth` / `AnchorYear`; payload carries the id | P2-02 |
| 10 | **Sleep schedule as default quiet hours** — offer "use my sleep schedule"; quiet hours are opt-in and unseeded today | host ↔ Notifications | — | write `notification_quiet_hours` from the calendar's typical `BedTime` / `WakeUpTime` (one window per user, per-day values differ) | FO10 |
| 11 | **Category / activity budgets** — "1 h of YouTube today" | Tracking | Push, once per day per budget | check on ingest with a daily mark | new entity |
| 12 | **Drift from plan** — tracked activity doesn't match the running foreground block | Tracking × Planning | Push, opt-in, rate-limited | **not** in `ActivityTimeRecordedEventHandler` (single on purpose; carries day totals, not the current window) — needs a Core seam for "current block" | care: easy to make naggy |
| 13 | **Account deletion grace period** — soft delete, undo link, finalise after 7 days (impulsive deletes) | host | Email | daily sweep (survives restarts) rather than `RunOnceAt`, which needs boot re-registration like `TimerBoundaryAlarmRegistrar` | GDPR wording: erasure "without undue delay" |
| 14 | **Google Calendar auto-sync** of today / tomorrow, plus "reconnect Google Calendar" when the refresh token fails | host | Bell + email on failure | queue + job, like the suggestion-pattern refresh | P1-03's reminder override |
| 15 | **Re-engagement email** to users away for weeks | — | — | **Don't** without explicit opt-in: marketing under §116 zák. 452/2021 | — |
