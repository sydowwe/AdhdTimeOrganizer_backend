# P3-04 — Evening shutdown: set up tomorrow before bed

> Paste into a fresh session. Read `review/notifications-opportunities/_SHARED-CONTEXT.md` first. Backlog #1.
> Framework (one kind) → host → frontend.

## Why

Planning tomorrow the evening before is one of the few ADHD strategies that reliably helps, and the app already has
everything it needs to prompt it at the right moment. **The scope is the "tomorrow" half only.** A nightly
plan-vs-actual recap is a list of what didn't happen, delivered as the user winds down. FO3 refuses to turn slips into
a score (`ITodoListItemPlanSource`: the counts "must never reach a response"), and the evening shouldn't either.

## Template: copy the morning briefing whole

`AdhdTimeOrganizer/infrastructure/jobs/MorningBriefingJobHandler.cs`,
`application/service/morningBriefing/MorningBriefingComposer.cs` + `MorningBriefingNotifier.cs`,
`IntegrationTests/Endpoints/MorningBriefingJobTests.cs`. Reuse the whole shape:
- a 15-min sweep over paged users, with zones failing open to UTC;
- a decided-once mark written with `ExecuteUpdate`;
- a `LateBy` retry only after a failed dispatch;
- skip on an away day and while the welcome-back card is open;
- a bulk composer reading the slices directly.

Register in `PortalScheduledJobsRegistrar`, add the key to `ModuleWiringTests`.

## What differs from the morning

- **The moment** is bed time minus an offset. Bed time is the day's `Calendar.BedTime`, else `User.DefaultBedTime`. A
  bed time at or before wake-up means after midnight (`UserSleepWindow`, `DayPacking.WindowEnd`), so **day D's evening
  can fall on calendar date D+1** (bed 01:30 → sent 00:30 on D+1). The sweep must check both today and yesterday as
  the day whose evening it may be, and it marks **D** (`User.EveningShutdownHandledFor`).
- Most users never set a bed time, so 00:00 makes the default send 23:00. The user's knob is
  `EveningShutdownOffsetMinutes` (default 60, range 30–180).
- **Opt-in**: `User.EveningShutdownEnabled = false`.
- **Quiet hours:** a deferred evening push lands the next morning next to the briefing, which is worse than nothing.
  If the moment is inside the user's window (`IQuietHoursReader` + `QuietHoursPolicy.IsWithin`), mark D and send
  nothing.
- Today **or tomorrow** an away day (`DayType.IsAway()`) → nothing.

## Content

Counts only, like `MorningBriefingPayload`:
`EveningShutdownPayload(DateOnly Tomorrow, int TomorrowBlocks, TimeOnly? TomorrowFirstBlockAt, int DueTomorrow, int LeftOpenToday)`.
- `TomorrowBlocks`: `PlannerTaskCarryOverRule.IsOpen` blocks on D+1. **Zero is the headline:** "nothing planned for
  tomorrow yet".
- `DueTomorrow`: open to-dos due D+1, timed and untimed (the user plans tonight what rings tomorrow).
- `LeftOpenToday`: open blocks on D, worded as an offer ("move 2 to tomorrow?") and linked to the carry-over / re-plan
  flow (FO4). P1-04's `PlannedBlocksMissed` already raised these during the day, so this is the one decision point,
  not another reminder.
- Send only when actionable: `TomorrowBlocks == 0 || LeftOpenToday > 0`.

Kind `EveningShutdown`: push on, email off, **held while away**, no action buttons. Deep link to tomorrow's planner.

## Frontend

A toggle and an offset next to the morning briefing's. ⚠ **P2-01's frontend half hasn't landed:**
`src/app/notifications/notificationTypeMeta.ts` has no `MorningBriefing` and nothing in `src/` reads
`morningBriefingEnabled`. Do both in one pass ([P2-01-frontend-prompt.md](P2-01-frontend-prompt.md)).

## Tests (copy `MorningBriefingJobTests`, fixed clock, two zones)

- Sent at bed − offset with the right counts, then marked.
- The second run sends nothing.
- Bed 01:30 → sent at 00:30 on D+1, and D is the day marked.
- Disabled by default.
- Tomorrow away → nothing.
- Tomorrow planned with nothing left open → nothing.
- Moment inside quiet hours → nothing, and marked.
- A failed dispatch is retried within `LateBy`.

## Open decisions

1. A planned tomorrow: no push (recommended), or a one-line "tomorrow starts 08:00" preview?
2. Offset default and range.
3. `LeftOpenToday`: a one-tap "carry all to tomorrow" action (P2-03 shape, a Planning handler), or a deep link only?
