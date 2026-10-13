# P3-01 — Quiet hours that follow the sleep window

> Paste into a fresh session. Read `review/notifications-opportunities/_SHARED-CONTEXT.md` first. Backlog #10.
> Framework (one column + a DTO field) → host → frontend.

## Why

Quiet hours are opt-in and never seeded, so for a user who never finds the setting nothing is ever deferred: a
sweep-raised push goes out whenever its sweep notices, night included. One push at 2 a.m. is how a user decides to
turn push off for good, and that takes every other notification with it. FO10 already asks the user for exactly what a
quiet window needs.

## Facts

- Sleep window: `User.DefaultWakeUpTime` / `DefaultBedTime` (Core, FO10), read through
  `AdhdTimeOrganizer.Core/domain/model/entity/user/UserSleepWindow.cs` (fallback 08:00 / 00:00). Bed at or before wake
  means after midnight. **No ordering rule**, so bed == wake is accepted (`UpdateUserPreferencesValidator.cs:29`).
- Written by `AdhdTimeOrganizer/application/endpoint/user/command/settings/UpdateUserPreferencesEndpoint.cs`; its
  `AfterUpdateAsync` already re-syncs future calendar days when a sleep-window field or the zone is sent. That hook is
  where the quiet window is re-derived too.
- Quiet hours: one row per user, `NotificationQuietHours(UserId, StartMinute, EndMinute)`
  (`framework/Sydowwe.Notifications/domain/entity/NotificationQuietHours.cs`). The minutes are in the user's own zone,
  resolved per read. `Start > End` is overnight. Set/Get/Delete live in `…/application/endpoint/quietHours/`; Set
  rejects `Start == End` and upserts through `BaseUserSettingsUpsertEndpoint` (which handles the insert race). The host's
  `AppDbContext` maps the table.
- Effect: push and email are **deferred**, never dropped, and the bell row is immediate. The Reminders scan also holds a
  due occurrence until the window ends. `TimerBoundary` is exempt (`QuietHoursExemptTypes`).
- Per-day `Calendar.WakeUpTime` / `BedTime` overrides exist, but a window is one per user. Mirror the **default**
  window only, and say so in the UI. Don't try to make quiet hours per day.

## Design

1. Mapping: window = `[bed, wake)`. 23:00/07:00 → 1380→420 (overnight); the fallback 00:00/08:00 → 0→480.
2. **Follow, don't copy.** Recommended: a flag on the module row (`NotificationQuietHours.FollowsHostSchedule`, name
   it better if you can). The framework Set endpoint clears it, so a manual edit stops the following by construction.
   The host sets it whenever it writes the row. Add it to `NotificationQuietHoursDto` so the UI can show the toggle.
   The alternative, a flag on `User`, relies on the frontend clearing it on a manual edit, and nothing enforces that.
3. Host: an endpoint to switch following on (writes the row from the current window, flag set), and in
   `UpdateUserPreferencesEndpoint.AfterUpdateAsync` re-derive the row when a sleep-window field was sent **and** the
   row's flag is set.
4. **Offer it, never seed it.** A silent default changes delivery for existing users: an early-morning to-do reminder
   whose lead lands inside the window would arrive at the window's end, after the item was due. Offer it on
   `QuietHoursCard` and right after the user grants push permission.

## Frontend (`C:\Users\jakub\WebstormProjects\AntiProcrastinationApp-frontend`)

- `src/_common/modules/notifications/component/QuietHoursCard.vue`: a "follow my sleep schedule" toggle; the manual
  fields are read-only while it's on.
- `…/notifications/composable/UsePushNotifications.ts`: the offer after subscribing.
- `src/core/user/component/settings/SleepWindowField.vue`: one line saying quiet hours follow it, when they do.

## Tests

- Switching on writes `[bed, wake)`, covering the overnight case and the fallback.
- A sleep-window change re-derives a following row and leaves a manual one untouched.
- A manual Set clears the flag.
- bed == wake takes the decided path.
- A push raised inside the derived window gets `DeferredUntil` = wake (copy `QuietHoursUserZoneAndLateDeliveryTests`).
- Account erasure still removes the row (`NotificationSubjectDataEraser`).

## Open decisions

1. bed == wake: delete the row (no quiet hours), or refuse to follow with a message.
2. Flag on the module row (recommended) or on `User`.
3. Pad the window (start before bed)? Recommended no: [P3-04](P3-04-evening-shutdown.md) sends at bed − 60.
