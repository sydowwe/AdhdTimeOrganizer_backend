# P3 — Backlog

> Read `review/notifications-opportunities/_SHARED-CONTEXT.md` first. Each idea here is a sketch, not a prompt; promote
> one to its own `P*.md` file when you pick it up. P2-02 (bell-only kinds) and FO10 (a real bed time) have both
> landed, so nothing below is blocked any more.

## Promoted 2026-10-13

Recommended order: P3-01 and P3-02 (small, and they make existing pushes better), then P3-03 and P3-04. The rest are
features in their own right; decide them as features.

| Prompt | Backlog # |
|---|---|
| [P3-01 Quiet hours follow the sleep window](P3-01-sleep-window-quiet-hours.md) | 10 |
| [P3-02 Streak milestones and new bests](P3-02-streak-milestones.md) | 3 |
| [P3-03 Stopwatch check-in](P3-03-stopwatch-check-in.md) | 5 |
| [P3-04 Evening shutdown (tomorrow half)](P3-04-evening-shutdown.md) | 1 |
| [P3-05 Weekly lines in the morning briefing](P3-05-briefing-weekly-lines.md) | 7, 6, 9 — folded into the briefing, not three kinds |
| [P3-06 Routine items with a time of day](P3-06-timed-routine-items.md) | 4 (the recurring-reminder DST fix it needed landed 2026-10-13) |
| [P3-07 Account deletion grace period](P3-07-account-deletion-grace.md) | 13 |
| [P3-08 Daily activity budgets](P3-08-activity-budgets.md) | 11 |
| [P3-09 Google Calendar auto-sync](P3-09-google-calendar-auto-sync.md) | 14 (the two sync bugs it found were fixed 2026-10-13) |

## Not promoted

- **#2 Weekly review email:** a four-slice aggregation plus an email template, for a surface the client already has
  (the weekly routine review card). At most a bell pointing to that card.
- **#8 Weekend weather window:** the leisure draw already ranks by today's weather when the user looks (F2-04). A push
  adds the only outbound HTTP to a sweep, plus rate limits, for little gain.
- **#12 Drift from plan:** needs a Core seam for "the current block", and it's the notification most likely to get
  push turned off.
- **#15 Re-engagement email:** marketing under §116 zák. 452/2021; needs explicit opt-in.

## The original sketches

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

Where a promoted prompt disagrees with its sketch, the prompt wins. The sketches for #4 (it is a dispatch guard, not a
resolver), #5 (its own kind, not a `TimerBoundaryPhase` value) and #11 (History, not Tracking; the web extension
attributes nothing) were wrong.
