# P2-04 — Tracker went silent

> Paste into a fresh session. Read `review/notifications-opportunities/_SHARED-CONTEXT.md` first.

## Why

When the desktop agent, the browser extension or the Android app stops reporting — uninstalled, logged out, token
expired, crashed — nothing errors. The tracking dashboards just quietly go empty, and the user concludes the feature
is broken or that they did nothing. It is the same silent-failure class this codebase guards against everywhere
else, only on the user's side.

## Facts

- Three ledgers: `DesktopActivityEntry` (RANGE-partitioned on `RecordDate`), `WebExtensionActivityEntry`
  (partitioned; hand-written filter `RecordDate >= CurrentPartitionDate && user`), `AndroidSessionData`
  (`SessionStartUtc`, not partitioned).
- Ingest: the desktop heartbeat, the web-extension heartbeat, the Android sync (all in
  `AdhdTimeOrganizer.Tracking/application/endpoint/*/command/`).
- `User.LastActiveAt` is the "the person is around" signal (FO5); tracker tokens never stamp it, so it measures the
  web app, not the trackers.
- Tracking's only job today: the 03:30 UTC retention purge (`TrackingScheduledJobsRegistrar.cs:27`).

## Design

1. **Keep a last-seen per user per source** instead of scanning partitioned ledgers: a Tracking-owned table
   `TrackerSourceStatus(UserId, Source, LastSeenAt, SilenceNotifiedAt)`, upserted on ingest and **throttled** like
   `LastActiveStampMiddleware` (at most once per hour per user/source; heartbeats are frequent). Bonus: the settings
   screen can show "desktop agent — last report 3 min ago".
2. **Rule:** the source reported within the last 30 days, nothing for 48 h, and `LastActiveAt` within 24 h (the user is
   here, the tracker isn't). Notify once per silence; clear `SilenceNotifiedAt` on the next report.
3. Kind `TrackerSilent`, payload `(string Source, DateTime? LastSeenAt)`. Push + bell; email opt-in. Held while away:
   **no** — the rule already requires the user be present, and it is operational.
4. Daily is enough; zone-aware (15-min sweep at a local daytime hour) is nicer — decision.
5. Backfill `LastSeenAt` once from the ledgers in the migration or a one-off, so the feature doesn't fire for everyone
   on day one (or seed `SilenceNotifiedAt = now`).

## Tests

A source that reported 3 days ago for an active user → one notification, marked; a second run → nothing; a new report
clears the mark; an inactive user → nothing; the ingest upsert is throttled (two heartbeats in a minute → one write).
