# P3-09 — Google Calendar: sync by itself, and say when the connection dies

> Paste into a fresh session. Read `review/notifications-opportunities/_SHARED-CONTEXT.md` first. Backlog #14.
> Host (service, job, one kind) → framework (the kind) → frontend. The two sync bugs this used to list (DST offset,
> deleted blocks left on Google) were fixed in the endpoint on 2026-10-13; carry the fixes into the extracted service.

## Why

Sync is a button per day today. A user who forgets to press it has a phone calendar showing yesterday's plan, which
is worse than an empty one. When Google revokes the token, nothing tells the user; the next manual sync just fails.

## Facts

- `POST calendar/{id}/sync-to-google` (`AdhdTimeOrganizer/application/endpoint/calendar/SyncCalendarToGoogleEndpoint.cs`):
  - inserts or updates the day's pushable tasks (not cancelled, not background) and stores `PlannerTask.GoogleEventId`;
  - deletes the events of tasks that became unpushable;
  - turns off Google's own alerts when the portal reminds (P1-03, `GoogleReminders`).
- Token: `User.GoogleCalendarRefreshToken` (Core). Service: `IGoogleCalendarService`
  (`AdhdTimeOrganizer/domain/extServiceContract/`, impl `infrastructure/extService/GoogleCalendarService.cs`).
  Connect, disconnect and status live in `application/endpoint/user/command/googleCalendar/`.
- Each event carries a private extended property marking it as the sync's own, plus its task id. After the upserts,
  the sync lists the day's marked events and deletes those no pushable block owns (`RemoveOrphanedEventsAsync`).
  That is how deleted and moved blocks, cascades included, leave Google. Events pushed before the marker existed are
  untagged and never touched.
- Instants go through `WallClockZone.ToUtc(date, time, zone)` per date. An update Google answers 404/410 becomes an
  insert.
- 401/403 returns Conflict with "reconnect", but the token is neither cleared nor flagged, so the status endpoint
  still reports "connected".
- Tests: `SyncCalendarToGoogleTests` runs a real `CalendarService` over `FakeGoogleCalendarTransport`. Reuse it.
- `SuggestionPatternRefreshQueue` (dirty set + job) is the precedent the backlog names, but its set is **in-memory**:
  fine for a view with a daily full refresh, wrong for a sync that must survive restarts.

## Design

1. Extract the sync into a host service, `SyncDayAsync(userId, date)`, shared by the endpoint and the job. Move the
   whole endpoint body, orphan sweep included; it already reconciles, so the job needs no tombstones.
2. Job `Portal.GoogleCalendarSync`, every 15 minutes:
   - for connected users with auto-sync on, sync today and tomorrow (user's zone) when they changed since
     `Calendar.GoogleSyncedAt`, compared with the max `ModifiedTimestamp` of the day's tasks and calendar row;
   - ⚠ `ExecuteUpdate` writes and deletes don't bump `ModifiedTimestamp`, so also reconcile today unconditionally
     once an hour (or every run, quota permitting);
   - one scope and one `CalendarService` per user, so one user's failure stays isolated.
   - Register it in `PortalScheduledJobsRegistrar`, with the key in `ModuleWiringTests`.
3. Token failure (`GoogleApiException` 401/403, or `TokenResponseException` `invalid_grant`):
   - set `User.GoogleCalendarNeedsReconnect`, or null the token (decision), and stop syncing that user;
   - notify **once** with kind `GoogleCalendarDisconnected`: push + bell, not held while away. Email on by default is
     a decision; the argument for it is `ScheduledJobFailed`'s: a silently dead integration is what the bell misses;
   - reconnecting clears the flag, and the status endpoint reports it.
4. `User.GoogleCalendarAutoSync`: on by default for new connections. Existing connections are a decision.

## Frontend

An auto-sync toggle and a "reconnect" banner where Google Calendar is managed. A `notificationTypeMeta.ts` entry that
deep-links to reconnect.

## Tests (`FakeGoogleCalendarTransport`)

- The job syncs a changed day and skips an unchanged one.
- The job removes the event of a block deleted by an activity-delete cascade (the endpoint tests already pin a plain
  delete, a move, DST and a 404 on update).
- 401 → flag set, one notification, no attempt on the next run.
- Reconnecting clears the flag.
- The manual endpoint still works through the shared service.

## Open decisions

1. On a token failure: flag it or null the token.
2. Email default for `GoogleCalendarDisconnected`.
3. Auto-sync for already-connected users: on or off.
4. Sync window: today + tomorrow, or the whole visible week.
