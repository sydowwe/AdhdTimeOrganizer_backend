# P3-06 — Routine items with a time of day ("meds at 8:00")

> Paste into a fresh session. Read `review/notifications-opportunities/_SHARED-CONTEXT.md` first. Backlog #4.
> Feature-sized: Routines, the host catalog and the frontend. (The framework's DST fix this used to start with landed
> on 2026-10-13.)

## Why

A recurring personal reminder already rings at 8:00, but it rings whether or not the meds were taken. A routine item
that knows its time rings only while it's undone, and its lock-screen Done button ticks it off. That's the version an
ADHD user keeps switched on.

## Facts

- Item: `RoutineTodoList : BaseTodoListItem` (`AdhdTimeOrganizer.Routines/domain/model/entity/RoutineToDoList.cs`). It
  has `TimePeriodId`, `Streak`, `SuggestedDays` and `SuggestedDayOfMonth`, but **no time field**. "Done this cycle"
  is `IsDone`, and a period reset reopens it.
- **The toggle lives in the endpoint.** `RoutineToggleIsDoneTodoListEndpoint` runs the lazy catch-up reset (with its
  lost-race recovery), `RoutineResetService.UpdateItemStreak`, the done-changed event and the period-ended
  notification. A lock-screen Done owes all of that. Extract it into a Routines service the endpoint and the action
  handler both call; don't copy it.
- **P1-02's shape is a guard, not a resolver.** `TodoListItemDueReminderSync` registers an explicit recipient, because
  the module keeps resolver-backed reminders off `my-upcoming`, snooze and dismiss (see the sync's doc comment). An
  `IReminderDispatchGuard` (`TodoListItemDueReminderGuard`) retires stale occurrences. Copy both.
- **Recurrence keeps a wall clock only when given a zone.** `ReminderSchedule.RecurringInterval(…, timeZoneId)`
  steps the anchor's local time in that zone (`ReminderOccurrenceCalculator`). Without a zone it adds days to the
  anchor's fixed UTC offset and drifts an hour at each DST switch. Pass the owner's zone, as
  `Planning/application/service/ReminderRegistrationService.BuildSchedule` does. `RecurringCron` is UTC: don't use
  it for this.
- Actions: an `INotificationActionHandler` (precedent `TodoItemDueNotificationActionHandler`) plus an entry in the host
  catalog's `Actions`. Snooze belongs to the Reminders module and only shows on reminder-raised pushes, which this is.

## Design

1. `RoutineTodoList.ReminderTime TimeOnly?` (`TimeDto` in DTOs). Null means no reminder.
2. **Registration:** one daily recurring reminder per timed item, with `timeZoneId` set to the owner's zone, keyed
   `(Routines, RoutineTodoList, id, "time-of-day")`. A Routines sync service (copy `TodoListItemDueReminderSync`):
   - registers on create and update;
   - cancels on item delete, period delete and activity delete/archive/merge. Find every path: a DB cascade never
     reaches the module, so read the ids before the delete and cancel after the commit;
   - re-syncs everything on a zone change, re-anchoring each item's `ReminderTime` in the new zone.
3. **Guard at dispatch:** deliver only if the item exists, isn't done, and neither it nor its period is hidden; today
   (in the user's zone) isn't an away day (FO1); and, when `SuggestedDays` or `SuggestedDayOfMonth` is set, today is
   one of them.
4. Kind `RoutineItemDue`, payload `(long RoutineTodoListId, string? Title, long PeriodId)`. The title is the user's own
   (precedent `TodoItemDuePayload`).
   - Push on, email off, **not** held while away (the user asked for it).
   - Actions `[Done, Snooze(10)]`.
   - Add an `IReminderKindCatalog` + `IReminderLabelResolver`, so it shows on `my-upcoming` and can be muted before
     it ever fires.

## Frontend

A time picker on the routine item dialog, and a `notificationTypeMeta.ts` entry deep-linking to the routine.

## Tests

- The registered definition carries the owner's zone and its next occurrence is `ReminderTime` local (copy
  `ReminderRegistrationTests.RecurringReminder_IsRegisteredInOwnersZone`).
- Guard: done, hidden, away day, non-suggested day, deleted.
- Done action ticks through the shared service: streak scored, period-ended raised when the tick triggers a lazy reset,
  event published (assert on rows).
- A zone change re-syncs every item.
- Catalog and vocabulary tests.

## Open decisions

1. A weekly item with no suggested day: ring daily until done, or only on the period's first day?
2. Snooze length.
