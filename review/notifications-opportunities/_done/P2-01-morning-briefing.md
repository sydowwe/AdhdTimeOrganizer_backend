# P2-01 — Morning briefing: one push instead of several

> **Status: backend done (2026-10-11).** Decisions taken: (1) only mornings with content get a briefing — "no plan
> yet" appears only beside something else; (2) option 1, `User.MorningBriefingEnabled` in Core, **on by default**;
> (3) P1-01 B's nudges stay separate. Setting: on/off + `MorningBriefingOffsetMinutes` (0–180). A morning is
> decided once (`User.MorningBriefingHandledFor`) — sent, empty, away day or welcome-back card — so nothing fires
> later that day; only a failed dispatch is retried, within 3 h. Frontend: [P2-01-frontend-prompt.md](P2-01-frontend-prompt.md).

> Paste into a fresh session. Read `review/notifications-opportunities/_SHARED-CONTEXT.md` first.
> Best after [P1-02](P1-02-todo-due-reminders.md), which deliberately leaves date-only to-dos to this briefing.

## Why

Separate morning signals live in separate slices, and each would otherwise become its own push: routines suggested
for today, today's plan, to-dos due today, overdue to-dos, blocks to carry over. For ADHD users, notification
fatigue is the failure mode that turns every channel off. One well-timed summary that opens the right screen beats
five pings.

## What it contains

| Section | Source | Read through |
|---|---|---|
| Today's plan: foreground block count, first start time; "no plan yet" | Planning | `Calendar` + `PlannerTask` (host can query directly) |
| Blocks to carry over from yesterday | Planning | the query behind `GetCarryOverCandidatesPlannerTaskEndpoint` |
| To-dos due today (date-only) and overdue count | TodoLists | the user's-today rule `GetDashboardTodoListItemEndpoint` uses; overdue links to the `RescheduleOverdue` renegotiation |
| Routines suggested for today | Routines | `RoutineSuggestedDay` — the rule `RoutineSuggestedTodayJobHandler` uses |

## Where it lives

**The host.** Only the host sees every slice without a seam, and `AdhdTimeOrganizer/application/service/welcomeBack/WelcomeBackPile.cs`
is the precedent for a host service reading TodoLists, Planning and Routines directly. Handler in
`AdhdTimeOrganizer/infrastructure/jobs/`, registration in `PortalScheduledJobsRegistrar`, key in `ModuleWiringTests`.

## Timing

Copy `RoutineSuggestedTodayJobHandler` exactly: 15-min cron, zones via `IUserTimeZoneReader`, the day's `WakeUpTime`
via `ICalendarDayLookup` (including its behaviour when the day has no calendar row), and a per-user per-local-date
mark written right after a successful send (new column, e.g. `User.MorningBriefingSentFor DateOnly?`, or a small
host table).

## What to send, and when not to

- Kind `MorningBriefing`, payload counts only: `(DateOnly Date, int Blocks, TimeOnly? FirstBlockAt, int CarryOver,
  int DueToday, int Overdue, int RoutinesSuggested)`.
- Held while away: **yes**. Email: **off** (a daily mail is noise; opt-in remains).
- Nothing to say → send nothing (or a single "No plan yet — plan your day?" — decision).
- Welcome-back card pending (`User.WelcomeBackAwayFrom` set) → skip; that card *is* the briefing for a returning user.
- User setting: on/off (+ optional offset from wake-up).

## The routine digest overlap

With the briefing on, `RoutineSuggestedToday` is a duplicate. Options:

1. **Recommended:** a Core-level flag (`User.MorningBriefingEnabled`, Core because Routines can see Core but not the
   host) that `RoutineSuggestedTodayJobHandler` checks to skip the user.
2. Retire `RoutineSuggestedToday` entirely in favour of the briefing.

Don't have the host write `SuggestedTodayNotifiedFor` to suppress the routine sweep — that is a cross-slice side effect
nobody would find.

## Tests

Fixed clock, users in two zones: one notification at wake-up with the right counts; the mark is set; a second run sends
nothing; an empty day sends nothing (or the "no plan" variant); a user with the welcome-back card pending gets
nothing; with the briefing on, the routine digest is not sent.

## Open decisions

1. The "no plan yet" variant.
2. Option 1 vs 2 for the routine digest.
3. Whether [P1-01 B](P1-01-fix-existing-wiring.md)'s routine nudges also fold in here.
