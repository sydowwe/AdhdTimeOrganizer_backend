# P3-08 — Daily activity budgets ("1 h of YouTube today")

> Paste into a fresh session. Read `review/notifications-opportunities/_SHARED-CONTEXT.md` first. Backlog #11.
> Framework (one kind) → History → frontend. New entity.

## Why

The time a user would set a budget for is the time they lose without noticing: the app opened "for a minute". The
tracker already sees it happen, and one well-timed nudge is worth more than the evening's dashboard.

## Facts

- `ActivityTimeRecordedEvent` (`AdhdTimeOrganizer.Core/application/event/ActivityTimeRecordedEvent.cs`) carries
  **whole-day totals per activity, across every source**: History's
  `IActivityTimeAttributionSink.TotalSecondsBetweenAsync`, read after the save. That's exactly the number a budget
  compares against. Publishers:
  - the desktop heartbeat (every minute);
  - the Android sync (today only);
  - History's create paths (`ActivityTimeRecordedPublisher`), which can be **any day**: a hand-log for yesterday also
    publishes.
- The existing host handler (`AdhdTimeOrganizer/application/eventHandler/ActivityTimeRecordedEventHandler.cs`) is
  single *because completion is exclusive*. A budget check is independent, so a second subscriber is fine. But it
  runs **inside the heartbeat request** (`Mode.WaitForAll`), every minute, so it must be one indexed query and send
  nothing unless a threshold is crossed.
- **The web extension doesn't attribute or publish.** `WebExtensionDataHeartbeatEndpoint` only stamps last-seen. A
  website in the browser never becomes activity time, so v1 budgets cover desktop apps, Android apps, timers and
  logs. Say so in the UI rather than let "YouTube" silently never trigger.
- Cross-slice event handlers log and swallow (CLAUDE.md, *Silent failures*), so the tests must assert on rows.

## Design

1. **Owner: History** (it owns activity time). Entity `ActivityDailyBudget : BaseEntityWithUser` with:
   - `ActivityId`. It joins the **Activity FK family**: read `docs/claude/persistence.md#delete-behaviour--the-activity-fk-family`,
     and handle archive and merge through `IActivityReferenceSource`.
   - `Limit` as an `IntTime` (a duration, per CLAUDE.md).
   - `NotifiedFor DateOnly?`.

   CRUD goes through the endpoint bases (`framework/Sydowwe.Framework/application/endpoint/base/`); the global user
   filter scopes the reads.
2. Handler in History: proceed only when `Day` is the user's **today** (resolve the zone). Load budgets for the
   event's activity ids where `NotifiedFor != Day`. For each total ≥ limit, notify, then mark with `ExecuteUpdate`,
   and only after a successful dispatch (the one-shot rule).
3. Kind `ActivityBudgetReached`, payload `(long ActivityId, string? ActivityName, int LimitMinutes, int TotalMinutes)`.
   - Push on, email off, not held while away (tracked time means the user is present).
   - Neutral wording: "1 h of YouTube today, the limit you set". Never "exceeded".
   - Inside quiet hours: skip (a budget push the next morning is stale), the same pattern as
     [P3-03](P3-03-stopwatch-check-in.md).
4. Category budgets (summing the category's activities) are a decision. v1 is per activity.

## Frontend

A budget field on the activity detail and a list in settings. A `notificationTypeMeta.ts` entry linking to the
activity's day in the tracking dashboard.

## Tests

- A heartbeat that crosses the limit → one notification and the mark set.
- The next heartbeat → nothing.
- The next day → it fires again.
- A hand-log for a past day → nothing.
- An Android sync crossing → it fires.
- Another user's budget is untouched.
- Activity delete removes the budget; a merge moves or drops it per the FK family.
- A throwing handler doesn't fail the heartbeat (200, and the ledger row is saved).

## Open decisions

1. Per-category budgets now or later.
2. An optional early warning at 80%.
3. Whether hand-logged time should ever trigger (recommended: today only, as above).
