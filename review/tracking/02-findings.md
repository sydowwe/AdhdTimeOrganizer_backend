# Tracking — Findings (02)

Point-in-time snapshot, 2026-09-01. First review of this module — there is no prior `review/tracking/`
and no `[Trait("Status","KnownGap")]` test carries a tracking scenario, so nothing below is a re-flag.
The four standing caveats in `docs/testing.md` (registrar restart, purge predicate, `ExecutablePath`
ciphertext, seeder) are **previously known** and are not repeated here except where a live bug sits
inside one of them (`SEC-3`).

Severity: 🔴 blocker (legal / security / data-corruption) · 🟠 important (wrong behaviour or
maintainability hole) · 🟡 polish. 🟡 items are collected in the appendix.

Paths are relative to `AdhdTimeOrganizer.Tracking/` unless stated.

---

## 5. Security & authorization

### SEC-1 🟠 ✅ FIXED 2026-09-03 · The web-extension heartbeat is missing the `ActivityTracking` policy

`application/endpoint/webExtension/command/WebExtensionDataHeartbeatEndpoint.cs:10-24`

The endpoint carries `[AllowExtensionClients]` but **not**
`Policies(PortalAuthorizationPolicies.ActivityTracking)`. Its two siblings do
(`DesktopActivityHeartbeatEndpoint.cs:49`, `AndroidSyncEndpoint.cs:23`), and `docs/domain-map.md`
("Auth", line 189) states as fact that all three do.

`[AllowExtensionClients]` only *lifts* the blanket `DenyExtensionClients` policy the `Program.cs`
configurator attaches — it grants nothing
(`framework/Sydowwe.Framework/infrastructure/security/ExtensionClientAuthorization.cs:48-57`). What is
left on this endpoint is the configurator's default `Roles("User","Admin","Root")`. So:

- an ordinary **web** session JWT can `POST` browsing rows into the ledger — the case
  `ExtensionActivityTrackingTests.WebJwt_CannotAccess_ActivityTrackingPolicy_Returns403` asserts is
  refused, on the desktop route;
- an extension token that has lost the `ActivityTracking` role still gets through, which
  `Heartbeat_WithExtensionToken_ButNoActivityTrackingRole_Returns403` pins on the desktop route only.

Not a cross-user leak — `User.GetId()` scopes the write — but it is a hole in the "which clients may
report activity" decision the host deliberately owns, and it is the one ingest route with no test at
all (`TEST-1`).

**Fix:** add `Policies(PortalAuthorizationPolicies.ActivityTracking);` to `Configure()`, and move the
route onto a group the way the other two are (see `MIG-3` — this changes who gets a 403).

**Fixed 2026-09-03.** `Policies(PortalAuthorizationPolicies.ActivityTracking)` added to `Configure()`.
The route was **not** moved onto a group: there is no `ActivityTrackingWebExtensionGroup`, and the other
five web-extension endpoints all declare literal routes, so grouping this one alone would split the
folder's convention — that is a separate change across all six. `TEST-1`'s auth matrix is now covered by
three cases in `ExtensionActivityTrackingTests`
(`WebExtensionHeartbeat_WithExtensionToken_ReturnsOk`,
`WebExtensionHeartbeat_WithExtensionToken_ButNoActivityTrackingRole_Returns403`,
`WebJwt_CannotAccess_WebExtensionHeartbeat_Returns403`); the rest of `TEST-1` — the running-total
upsert, minute alignment, the `isFinal` latch — is still open. The `MIG-3` rollout risk stands: this is
live at deploy, not behind a log-only release.

`Doc impact: none` (the doc is right; the code is wrong).

### SEC-2 🟠 ✅ FIXED 2026-09-03 · Mapping create accepts another user's `activityId` / `roleId` / `categoryId`

`application/dto/request/desktop/CreateTrackerDesktopMappingRequest.cs:29-43`,
`application/dto/request/android/CreateTrackerAndroidMappingRequest.cs:23-35`,
`application/validator/desktop/CreateTrackerDesktopMappingValidator.cs`,
`application/validator/android/CreateTrackerAndroidMappingValidator.cs`

`ToEntity` copies the three raw `long?` FK ids straight through, and neither validator checks that any
of them belongs to the caller. The global query filter scopes *reads*; this is a write of a raw id into
a column whose target row genuinely exists, so the FK constraint is satisfied and nothing rejects it.

The consequence here is worse than the identical hole in TodoLists (`SEC-1` in
`review/todo-lists/02-findings.md`), because a mapping is a **standing rule**: every subsequent desktop
heartbeat that matches the pattern reaches
`DesktopActivityHeartbeatEndpoint.cs:87-91` and calls
`attributionSink.AttributeAsync(dbContext, userId, activityId, …)` — writing an attribution row for the
caller against **another user's** activity, and then publishing `ActivityTimeRecordedEvent` with it, on
every heartbeat, indefinitely. Read-back is blocked (`TrackerDesktopMappingResponse.Projection`'s
`e.Activity` navigation is filtered), so the user sees a mapping that looks broken rather than the other
user's name — which is why this would never be reported as a bug.

**Fix:** validate ownership in an `AfterMapping`/`ValidateAsync` hook —
`MoveTodoListItemEndpoint.cs:51-54` in TodoLists is the shape to copy. Better: lift it into a Framework
helper, since this is now the third slice with the same hole.

**Fixed:** through the guard History's `SEC-4` already put in Core rather than a fourth copy —
`IReferencedIdOwnershipService` gained `OwnsActivityRoleAsync` / `OwnsActivityCategoryAsync` beside the
existing `OwnsActivityAsync` (roles and categories are strictly per-user, so both are a plain `UserId`
comparison with no filter to lift). `application/endpoint/extensions/MappingTargetOwnershipExtensions.cs`
wraps the three checks into one call and is invoked from `BeforeMapping` on
`CreateTracker{Desktop,Android}MappingEndpoint`, so the row is never mapped, let alone added.
`BeforeMapping` rather than `AfterMapping`: the ids are the request's, not the entity's, and rejecting
before the insert keeps the failure off the change tracker. 404 per offending field, with the same
message for a missing id as for a foreign one — distinguishing them makes the endpoint an existence
oracle over another account's ids. The update DTOs carry none of the three ids (`CQ-10` is that gap),
so create is the whole write surface today.

Pinned by `TrackerMappingTargetOwnershipTests` (5 cases: foreign activity on both endpoints, foreign
role + category, and an own-activity control on each so the guard cannot degrade into a blanket 404).

`Doc impact: domain-map.md → Invariants (add "a mapping's activity/role/category is the author's own",
app-enforced)` — done.

### SEC-3 🟠 ✅ FIXED 2026-09-03 · `AndroidSessionData` is never purged — the retention job covers two of the three ledgers

`infrastructure/jobs/PurgeExpiredActivityTrackingEntriesJobHandler.cs:42-50`

The job deletes from `DesktopActivityEntry` and `WebExtensionActivityEntry` only. `AndroidSessionData`
— per-session app usage with a device id, keyed to a person, unmistakably personal data — has **no
retention path at all**: no purge, no partitioning, and none of the 2-year `CurrentPartitionDate` floor
that hides old web-extension rows from reads (`AppDbContext.cs:41,279-280`). It accumulates for the
lifetime of the account.

The job's own comment cites GDPR Art. 5(1)(e) and names the two tables, so the doc and the code agree
with each other — they are together incomplete. Deletion on account closure is presumably handled by
`UserDataExportAndDeletionTests`' classification, but storage limitation is a separate obligation from
erasure.

**Fix:** add a third `ExecuteDeleteAsync` over `AndroidSessionData` on `SessionStartUtc < cutoff` with
`IgnoreQueryFilters()`, and extend the log line. `MIG-4` covers the first-run blast radius.

**Fixed:** the handler now takes one `cutoffUtc = retention.CutoffUtc()` for the whole run (so the three
ledgers cannot disagree about where the horizon sits), derives the existing `cutoffDate` from it, and adds
`Set<AndroidSessionData>().IgnoreQueryFilters().Where(e => e.SessionStartUtc < cutoffUtc)
.ExecuteDeleteAsync(ct)`. The log line and its guard carry the third count. `CutoffUtc()` is
`DateTimeKind.Utc` (it is `DateTime.UtcNow.AddYears(-n)`), which is load-bearing against `timestamptz` in
production for the reason `CQ-1` explains, and the handler comment now says so. Also updated:
`TrackingScheduledJobsRegistrar.RetentionPurgeRegistration.Description` (it is what the scheduler
dashboard prints for this job) and `ActivityTrackingRetentionOptions`' XML doc, both of which named two
ledgers.

Two things deliberately left out of this change:

- **The index.** `android_session_data` is not partitioned, so this predicate is a real cross-user sweep
  and wants a plain `session_start_utc` index (the "Indexes that should exist" list, and `MIG-4`). That is
  a migration on the host and is rollout work, not part of the code fix. **Shipped 2026-09-03** in
  `20260903003933_TrackingLedgerReadIndexes`, batched with the other two indexes this review asked for —
  see "Indexes that should exist" below.
- **A test.** Nothing in the suite *executes* this handler — `RetentionPurgeJob_IsRegisteredAndScheduled`
  only asserts it was registered — so the new predicate has no row-level guard, exactly like the two that
  preceded it. `AUDIT-1`'s "log the run unconditionally" is untouched for the same reason.

`Doc impact: done` — `summary.md` → Bounded context (the Retention bullet now names all three ledgers and
which column each ages on); `domain-map.md` → Infrastructure (retention row) and the `AndroidSessionData`
entity row, which now says it is the one unpartitioned ledger. `DOC-3` closes with this.

### SEC-4 🟠 ✅ FIXED 2026-09-03 · User-authored regexes run un-timed on the ingest thread

`application/endpoint/desktop/command/DesktopActivityHeartbeatEndpoint.cs:141-153`

`MatchesString` calls `Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase)` with a pattern the user
stored on a mapping row and a value the agent sent — no `matchTimeout`, no `RegexOptions.NonBacktracking`,
and a fresh parse on every call. The loop is *entries × active mappings*, so a single mapping with a
catastrophic pattern (`(a+)+$` against a long window title) pins a request thread for that user on every
heartbeat, forever, at whatever cadence their agent posts. `Wildcard` builds a regex on every call too
(line 149) even though it is derived from a fixed escape.

Self-inflicted rather than cross-tenant — but it is an authenticated user turning an unattended,
auto-retrying client into a sustained CPU load, and nothing bounds it.

**Fix:** pass `TimeSpan.FromMilliseconds(100)` as the match timeout (and catch `RegexMatchTimeoutException`
as "no match"), or use `RegexOptions.NonBacktracking`; cache the compiled `Regex` per mapping row for the
lifetime of the request rather than re-parsing per entry.

**Fixed:** the three matching helpers moved into a request-scoped `PatternMatcher` (one instance per
`HandleAsync`) that caches the compiled `Regex` per `(pattern, matchType)`, so a pattern is parsed once
per request instead of once per entry × mapping. `Wildcard` and `Regex` now share one path built with
`new Regex(expression, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100))`, and
`RegexMatchTimeoutException` is caught as "no match" — a catastrophic pattern costs 100 ms per value
rather than a thread. `NonBacktracking` was not used: it would reject the lookarounds and backreferences
a user may legitimately have stored, and would change which existing mappings still match.
Compilation stays lazy inside the match, so an invalid user regex still throws at the same point it did.
`Exact` / `Contains` are untouched.

`Doc impact: summary.md → Gotchas (the "matched in memory, not in SQL" bullet gains the timeout)` — done.

### SEC-5 🟠 ✅ FIXED 2026-09-03 · No bound on ingest batch size or android session length

`application/validator/android/AndroidSyncValidator.cs`,
`application/validator/desktop/DesktopActivityHeartbeatValidator.cs:16`,
`application/validator/webExtension/WebExtensionHeartbeatValidator.cs:24`

None of the three ingest validators caps the number of items (`Sessions` / `Entries` / `Activities`),
and `AndroidSyncValidator` bounds neither `DurationSeconds` above nor the session instants to anything
near "now". `AndroidSyncEndpoint` only checks `end > start` and that the stated duration matches the
span within 2 seconds — so a ten-year session is accepted and stored.

That row is then read by `UnifiedActivityLoader.LoadAndroidAsync` →
`SliceIntoMinutes(start, end)` (`application/service/unified/UnifiedActivityLoader.cs:311-330`), which
allocates **one `SourceMinute` per minute of the session**. A single accepted decade-long session makes
every unified dashboard for that user allocate ~5M records and fall over — a stored, permanent
denial-of-service on the six merged endpoints, with the poison row invisible in the android dashboards
(which read it as one session).

**Fix:** cap collection sizes (a heartbeat is one minute — a few hundred entries at most); bound
`DurationSeconds` to 24h and reject sessions starting more than a small window in the future or before
the retention floor; defensively clamp `SliceIntoMinutes` regardless.

**Fixed 2026-09-03.** All four parts, with the caps collected in one place
(`application/validator/TrackingIngestLimits.cs`) rather than three literals:

- each validator caps its collection — 1000 android sessions per sync (a backlog upload after a long
  offline stretch, not a day's worth), 200 desktop entries and 200 web-extension activities per
  heartbeat (both cover a single minute);
- `AndroidSyncValidator` bounds `DurationSeconds` to 24h above as well as `> 0` below;
- `AndroidSyncEndpoint` bounds the span the two instant *strings* describe — which the validator cannot
  see, since it only has the stated `DurationSeconds` — plus a 5-minute future skew and the retention
  floor (`ActivityTrackingRetentionOptions.CutoffUtc`, injected). Those three join the existing
  unparseable / `end <= start` / duration-mismatch arms and **drop the offending session with a warning
  rather than failing the sync**: the batch is a backlog upload, so one bad row must not strand the rest
  of the device's history. The batch-size and duration caps stay a 400, because a payload that shape is
  a broken or hostile client, not one bad row.
- `SliceIntoMinutes` clamps to `24 * 60 + 1` slices regardless, for rows written before the gate.

The retention floor is a deliberate addition beyond the finding: without it a phone can write rows the
purge job (`SEC-3`) deletes on its next run, which reads as history nobody can explain. `MaxClockSkew` is
5 minutes rather than zero because device clocks drift and the sync is asynchronous.

Pinned by `TrackingIngestBoundsTests` (8 cases). Five of them assert on **stored rows**, not just the
status code, because a poison row is invisible in the android dashboards — they render it as one ordinary
session. One of those five asserts an ordinary session still lands, so the bounds cannot silently eat
normal traffic.

`Doc impact: domain-map.md → Invariants (an android session is at most one day and is not in the future)` — done, as two bullets (session bounds, payload size).

---

## 6. Code quality

### CQ-1 🟠 ✅ FIXED 2026-09-03 · `BaselineType.AllTime` throws in production and cannot throw in tests

`application/endpoint/desktop/query/dashboard/DesktopSummaryCardsEndpoint.cs:97`,
`application/endpoint/webExtension/query/WebExtensionSummaryCardsEndpoint.cs:125`,
`application/endpoint/android/query/AndroidSummaryCardsEndpoint.cs:97`

All three write `BaselineType.AllTime => (DateTime.MinValue, currentFrom, -1)` and hand that
`DateTime.MinValue` to EF as the lower bound of the baseline read. `DateTime.MinValue.Kind` is
`Unspecified`, and both `window_start` and `session_start_utc` are `timestamp with time zone`
(`Migrations/20260819234301_Initial.cs:638,1083`). Npgsql refuses to write an `Unspecified` `DateTime`
to `timestamptz` — the request 500s.

It cannot be caught here: `framework/Sydowwe.Framework.Testing/PostgresContainerFixture.cs:26` sets
`AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true)`, which makes `Unspecified` mean UTC.
`Program.cs` sets no such switch. **Every test in the solution runs a different date-time contract than
production**, and this is the first place that difference has produced a live bug. `BaseFocusMetricsEndpoint`
avoids it by construction (its `AllTime` resolves through `FirstActivityDayAsync` and falls back to
`req.DateFrom.AddDays(-MaxLookbackDays)`), which is also the fix.

**Fix:** replace `DateTime.MinValue` with the same bounded floor the focus-metrics base uses —
`currentFrom.AddDays(-DashboardDateRangeRules.MaxSpanDays)` — which fixes `PERF-6` at the same time. Then
decide separately whether the legacy switch should be removed from the test fixture (`MIG-5`); until it
is, no test can prove this class of bug is gone.

**Fixed** in the shared base (`CQ-18`). `AllTime` now resolves to `req.DateFrom.AddDays(-MaxSpanDays)`
— a `DateOnly` put through `DailyWindowSet.Resolve` on the user's zone, so what reaches EF is a
zone-derived UTC instant and there is no `Unspecified` `DateTime` on the path at all. That is also
`PERF-6`'s fix: the read is bounded at a year and a day instead of running from the beginning of time.
The divisor is still derived from the data actually found, so a user with three days of history is
still divided by three — the floor changes which rows are eligible, not how the mean day is taken.

`BaseFocusMetricsEndpoint` narrows its equivalent lookback further via `FirstActivityDayAsync`; the
cards deliberately do not. The start is clamped to the floor either way, and a day earlier than the
user's first has no rows, so the two produce the same numbers and the cards save a query per request.

**The 500 half is still untestable**, exactly as this finding says: under the fixture's legacy switch
the old code returned a well-formed 200. What is pinned instead is the floor —
`TrackingSummaryCardsBaselineTests.AllTimeBaseline_IsFlooredAtTheMaximumSpan_NotAtTheBeginningOfTime`
asserts on the printed average, which an 18-month-old row moves from 300 to 180. `MIG-5` is untouched
and is still the only thing that would make the date-time contract itself testable; `CQ-6`'s note about
this same pattern stands.

`Doc impact: none`

### CQ-2 🟠 ✅ FIXED 2026-09-03 · The summary-cards baseline ignores the per-day time-of-day window

`DesktopSummaryCardsEndpoint.cs:113-149`, `WebExtensionSummaryCardsEndpoint.cs:141-190`,
`AndroidSummaryCardsEndpoint.cs:108-138`

The current period is read through `windows.Restrict(...)` — the nights outside the user's time-of-day
window are dropped. The baseline is read with a bare `WindowStart >= from && < to` over the raw
envelope and is **never restricted**. On a 09:00–17:00 window that makes the baseline roughly three
times too large, so every card reports a fall of ~65% on a perfectly ordinary week.

`BaseFocusMetricsEndpoint.BuildBaselineAsync:146-150` gets this right and its comment states the rule
("a baseline computed over whole days would compare a working-day figure against a round-the-clock
one"). `domain-map.md:138-140` asserts the two agree: *"The baseline scales the same way summary-cards'
does … The two sit on one screen and must not disagree about what 'compared to last 7 days' means."*
They disagree today. `UnifiedSummaryCardsEndpoint.BuildBaselineAsync:144-147` also gets it right, so
three of five baselines are correct and the three oldest are not.

**Fix:** build a `DailyWindowSet` for the lookback (`DailyWindowSet.Resolve(lookbackStart, lookbackEnd,
req.From.ToTimeOnly(), req.To.ToTimeOnly(), timeZone)`) and `Restrict` the baseline rows through it —
i.e. make these three read the way `BaseFocusMetricsEndpoint` and the unified card already do. See
`CQ-18`: the real fix is one shared baseline.

**Fixed** as one shared baseline (`CQ-18`), which is the option this finding names. The lookback is now
a `DailyWindowSet` in its own right, and the base calls the source's `LoadAsync` for it — the same
method, with the same `Restrict`, that reads the span. There is no way to read one through the window
and the other around it, because there is only one read.

Pinned by `TrackingSummaryCardsBaselineTests.Baseline_IsRestrictedToTheUsersDailyWindow_NotTheWholeEnvelope`,
a theory over all three routes: 700 seconds inside a 09:00–17:00 window and 7000 in the night beside it,
asserting the card prints 100 rather than 1100. `DOC-2` closes with this, with no doc edit.

`Doc impact: none` (the doc states the intended rule; the code now matches it)

### CQ-3 🟠 ✅ FIXED 2026-09-03 · The android summary cards fold the weekday and the day span on UTC

`application/endpoint/android/query/AndroidSummaryCardsEndpoint.cs:118,124-126`

`var targetWeekday = from.DayOfWeek;` and `x.SessionStartUtc.DayOfWeek`, plus
`data.Min(x => x.SessionStartUtc).Date` for the `AllTime` divisor — all read UTC. The desktop and
web-extension twins route the identical code through `WallClockZone.FromUtc(…, timeZone)`
(`DesktopSummaryCardsEndpoint.cs:124-131`, `WebExtensionSummaryCardsEndpoint.cs:161-170`) and their
comments explain why: a 00:30 Tuesday session in Bratislava belongs to Monday.

`TrackingDashboardUserZoneTests.SummaryCards_MatchTheSameWeekdayBaselineOnTheUsersCalendar_NotUtcs`
pins this rule — against the **desktop** route only, so the android copy drifted out from under a guard
that was written for it.

**Fix:** thread `timeZone` into `GetBaselineAverages` and fold through `WallClockZone.FromUtc`, exactly
as the other two do.

**Fixed** by deletion (`CQ-18`): there is no android `GetBaselineAverages` any more. Both folds happen
once, in the base, through `WallClockZone.FromUtc`. The target weekday is now `req.DateFrom.DayOfWeek`
rather than the envelope's — the same value, read off the calendar date the user asked for instead of
recovered from an instant, which is how `BaseFocusMetricsEndpoint` and the merged card already read it.

Pinned by `TrackingSummaryCardsBaselineTests.AndroidBaseline_MatchesTheSameWeekdayOnTheUsersCalendar_NotUtcs`,
the android twin of the desktop-only guard this copy drifted out from under. Both sides of the reading
discriminate: `steady` catches the target weekday (the requested Tuesday opens at 22:00Z on Monday) and
`midnight` the per-row one (00:30 local Tuesday is still Monday in UTC).

`Doc impact: none`

### CQ-4 🟠 The android pie chart drops slices by default and its parts stop summing to its total

`application/endpoint/android/query/AndroidPieChartEndpoint.cs:61-72`

`var minPercent = req.MinPercent ?? 1.0;` — a filter is applied even when the client asked for none —
and the below-threshold apps are **discarded** rather than folded into an `Other` bucket. `totals` is
computed from `periodData` before the filter, so `sum(apps.seconds) < totals.totalSeconds` on almost
every real day, silently.

Both sibling pies do the opposite: no default threshold, and an explicit `Other` row
(`DesktopPieChartEndpoint.cs:70-89`, `WebExtensionPieChartEndpoint.cs:70-100`), and the unified one goes
out of its way to preserve the identity (`UnifiedPieChartEndpoint.cs:112-122` — *"The folded seconds
stay in the response rather than disappearing, so the items still sum to the totals the page prints
above them"*). This is the same class of arithmetic-identity break that `SecondsAllocator` exists to
prevent on the merged side.

**Fix:** drop the `?? 1.0` default and add the `Other` bucket, matching the other three.

`Doc impact: none`

### CQ-5 🟠 ✅ FIXED 2026-09-03 · The web-extension heartbeat is an N+1 read with a lost-update race

`application/endpoint/webExtension/command/WebExtensionDataHeartbeatEndpoint.cs:31-76`

One `FirstOrDefaultAsync` **per activity in the batch**, inside the loop, before a single
`SaveChangesAsync`. Two problems:

1. N round trips per heartbeat, once a minute, per user, per browser — the highest-frequency write path
   in the module.
2. Read-then-write with no unique-violation handling. Two heartbeats in flight for the same
   `(user, windowStart, domain)` — a retry after a timeout is the ordinary case for an unattended
   client — both see no row and both insert, violating
   `ix_web_extension_activity_entry_user_id_window_start_domain_re` and 500ing the whole batch. The
   partitioned table's `row_version` is explicitly *not* a concurrency token
   (`WebExtensionActivityEntryConfiguration.cs:19-21`), so nothing else catches it either.

**Fix:** load the window's existing rows once (`Where(x => x.UserId == userId && x.WindowStart == req.WindowStart)`
→ dictionary by domain), then insert/update in memory; and handle the unique violation as an idempotent
retry rather than a 500. Since the extension sends running totals, an `ON CONFLICT … DO UPDATE` upsert
is the honest shape.

**Fixed:** as written for the first half — one read for the window, `ToDictionary` by domain, insert/update
in memory, one save. `StringComparer.Ordinal`, because that is how Postgres compares the column: a
case-insensitive comparer would fold two domains the index keeps apart, which is the same bug in the
other direction. The unique index `(UserId, WindowStart, Domain, RecordDate)` already serves the read, so
this costs no new index — and it is what guarantees `ToDictionary` cannot throw on a duplicate key.

A newly inserted row is registered in the dictionary before the save, which closes a collision the finding
does not name: **two activities for one domain in a single payload.** Nothing in
`WebExtensionHeartbeatValidator` prevents it, and both copies used to be `Add`ed. Last-one-wins is the same
running-totals rule the update branch already applied across requests, not a new one.

`ON CONFLICT … DO UPDATE` was not used: EF has no first-class upsert, and hand-written SQL here would have
to name the `tracking` schema itself (the finalizing convention only qualifies EF-generated SQL) on a
partitioned table. The batched read plus the 409 below is the same guarantee without that.

The residual true race — two heartbeats in flight for the same window, both reading no row — is now a 409
through `IngestBatchSaveExtensions.TrySaveIngestBatchAsync` (`CQ-11`) instead of a 500. The retry finds the
row and takes the update branch, so it converges rather than looping.

Pinned by two cases in `TrackingIngestConflictTests`: the in-payload repeat folding to one row with the
later running total, and a second heartbeat for the same window updating the existing domain while
inserting a new one — the path the per-activity `FirstOrDefaultAsync` used to serve.

`Doc impact: domain-map.md → Invariants (one entry per (user, window, domain), and what the endpoint does
with a collision)` — done, as one bullet shared with `CQ-8`.

### CQ-6 🟠 ✅ FIXED 2026-09-03 · Android sync re-labels an offset-bearing timestamp as UTC instead of converting it

`application/endpoint/android/command/AndroidSyncEndpoint.cs:37-46`

```csharp
DateTime.TryParse(item.SessionStartUtc, null, DateTimeStyles.RoundtripKind, out var start)
…
start = DateTime.SpecifyKind(start, DateTimeKind.Utc);
```

`RoundtripKind` preserves the offset a client sent: `2026-09-01T10:00:00+02:00` parses to a `Local`-kind
`DateTime` whose value is the *local* 10:00. `SpecifyKind` then stamps `Utc` on it **without converting**
— the session is stored two hours late. Every dashboard, every band and every merged minute for that
session is then wrong, silently, and only for clients that send an offset. The field is named
`SessionStartUtc`, so this only bites a client that is being more correct than the contract asks.

**Fix:** parse with `DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal`, which converts
an offset-bearing value and treats a naked one as UTC — then the `SpecifyKind` call is unnecessary.

**Fixed 2026-09-03**, as written, and both `SpecifyKind` lines are gone. The culture argument went from
`null` (ambient) to `CultureInfo.InvariantCulture` at the same time — these two fields are a machine
contract and the server's locale has no say over them.

Ordering note: this was done **before** the rest of the batch on purpose. `SEC-5` had just added a
future-skew bound and a retention floor to this same loop, and until this fix those bounds were being
applied to an instant that could be off by the client's offset.

Pinned by `AndroidSyncSessionStorageTests.AndroidSync_ConvertsAnOffsetBearingInstant_RatherThanRelabellingIt`,
which posts one instant spelled twice — naked and `+02:00` — and asserts the two stored rows agree, then
that they agree with the instant the client meant. Verified to fail on the old code (`21:55` stored for a
`19:55Z` session) before the fix went back in. **The guard cannot fail on a UTC host**: the error is
exactly the server's own UTC offset, so a UTC CI box would have stored the right value by accident. This
is a second instance of the `CQ-1` / `MIG-5` pattern — the test environment running a different date-time
contract than production — and worth remembering if CI ever becomes the only place this suite runs.

`Doc impact: domain-map.md → Invariants (the two android session instants are read as UTC)` — done,
because "the field is named `SessionStartUtc` so we relabel it" was the reasoning that produced the bug.

### CQ-7 🟠 ✅ FIXED 2026-09-03 · Android sync does not de-duplicate within a batch

`application/endpoint/android/command/AndroidSyncEndpoint.cs:76-114`

`existingSet` is built from rows **already in the database**; `toInsert` is never checked against
itself. Two items in one request with the same `(PackageName, SessionStartUtc)` — a phone that
re-uploads its buffer, or two rows for one app in the same second — both go into `AddRange`, violate
`ix_android_session_data_user_id_device_id_package_name_session_` and 500 the whole sync. The device
then retries the same batch forever.

The unique index is the invariant `domain-map.md:53-54` calls "what makes the sync endpoint's re-send
idempotent" — the endpoint is idempotent *across* requests and not *within* one.

**Fix:** track the keys added so far in the same `HashSet` and count in-batch repeats as
`duplicatesSkipped`.

**Fixed:** as written — the set is renamed `seenKeys` (it is no longer only the stored ones) and the
`Contains` + implicit-add pair collapses into `if (!seenKeys.Add(key))`, so a key is recorded by the same
statement that tests it and the two cannot drift apart. `UserId` and `DeviceId` are fixed for the request,
so `(package, start)` is the rest of the index. In-batch repeats land in `duplicatesSkipped` alongside
cross-request ones: the client cannot tell the two apart and does not need to.

Pinned by two cases in `AndroidSyncSessionStorageTests` — the repeat itself (200 rather than 500,
`accepted: 1`, `duplicatesSkipped: 1`, one stored row) and a control that surrounds the repeat with
another session, so the dedup cannot degrade into dropping the batch around it.

`Doc impact: domain-map.md → Invariants (the uniqueness bullet now says the endpoint keys off the same
tuple within a batch, not just against stored rows)` — done. That bullet called the index "what makes the
sync endpoint's re-send idempotent", which was the sentence the bug hid behind.

### CQ-8 🟠 ✅ FIXED 2026-09-03 · The desktop heartbeat can violate its own unique index inside one window

`application/endpoint/desktop/command/DesktopActivityHeartbeatEndpoint.cs:69-85`, unique index at
`infrastructure/persistence/configuration/desktop/DesktopActivityEntryConfiguration.cs:39`

The index is `(UserId, WindowStart, RecordDate, ProcessName, WindowTitle)`. Every entry in a batch
shares `WindowStart`, so **two entries with the same process and the same window title in one heartbeat
collide** — which is exactly what two windows of one application on two monitors look like, and
`ActiveMonitor` (the field that distinguishes them, and the reason `DesktopProcessDetailsEndpoint`
reports a per-monitor breakdown) is not in the key. The whole window is lost with a 500 and the agent
retries it identically.

**Fix:** either group colliding entries in the handler before `Add` (summing their seconds, which is
what the ledger means), or widen the index to include `ActiveMonitor`. Grouping is the smaller change
and does not touch a partitioned table (`MIG-2`).

**Fixed** by grouping, the option that stays off the partitioned table. `CollapseCollidingEntries` folds the
batch on `(ProcessName, WindowTitle)` — `UserId`, `WindowStart` and the derived `RecordDate` are fixed for
the request, so that pair is the rest of the index — summing both second counts. Everything else, including
`ActiveMonitor`, comes from the **busiest** of the collided entries rather than the first, so the surviving
row says where the time actually went.

`ProductName` is the awkward part and is called out in the method's remarks: the pattern mappings match on
it but the index does not carry it, so a collision whose members disagree about it has its match decided by
the representative. That shape is pathological — same executable, same window title, different product name
— and the alternative to picking one is a row that cannot be stored at all.

Two deliberate additions beyond the finding:

- **The cross-request half.** Grouping fixes the collision *inside* a batch; an agent whose heartbeat timed
  out after it committed re-posts the same window and collides with its own first attempt. The handler now
  seeds a `seenKeys` set from the rows the window already holds and skips them — the same idiom, and the
  same variable name, as `AndroidSyncEndpoint` after `CQ-7`. **The skip covers the attribution with the
  entry**: the first attempt already called `AttributeAsync` for those seconds, so re-running the sink
  would count them twice. That is the whole reason the skip is in the loop rather than a filter before it.
- **The residual race** — two heartbeats genuinely in flight — is a 409 rather than a 500, through
  `CQ-11`'s wrapper.

Pinned by three cases in `TrackingIngestConflictTests`: the two-monitor fold (asserting the summed seconds
and that the busier entry's monitor survives), a control where the entries differ on an *indexed* field and
must stay two rows, and a re-posted window that must be a 200 whose seconds are not doubled. The fold case
discriminates — verified against a variant that groups on `ActiveMonitor` too, which stores 20 seconds
instead of 50. That variant is worth noting as the trap: with the `seenKeys` skip in place but the group key
wrong, the second window is *silently dropped* rather than 500ing, so the fold and the skip have to be read
together.

`Doc impact: domain-map.md → Invariants (one entry per (user, window, process, title), and that a collision
sums)` — done, as one bullet shared with `CQ-5`.

### CQ-9 🟠 ✅ FIXED 2026-09-03 · No length validation anywhere on the write paths

`application/validator/desktop/DesktopActivityHeartbeatValidator.cs:24-33`,
`application/validator/desktop/CreateTrackerDesktopMappingValidator.cs`,
`application/validator/android/CreateTrackerAndroidMappingValidator.cs` (and both update validators)

The desktop heartbeat validator checks `NotEmpty()` on `ProcessName` and `WindowTitle` and nothing on
`ProductName` or `ExecutablePath`; the four mapping validators check no length at all. The columns are
`varchar(255)` / `varchar(1024)` / `varchar(200)`, so anything longer is a Postgres `22001` → 500
instead of a 400 with a message. `WebExtensionHeartbeatValidator` is the one that gets this right
(`MaximumLength(255)` / `(2048)`), which is the proof it is an omission rather than a policy.

A second, quieter edge: `WindowTitle` is `varchar(1024)` **and** part of a btree index. A 1024-character
title of 3-byte characters is ~3 kB, over Postgres' ~2704-byte index-tuple limit — that insert fails too,
with a different error and the same 500.

**Fix:** add `MaximumLength` to every string rule on both ingest and mapping DTOs, matching the column
widths; consider truncating `WindowTitle` at ingest to keep it clear of the index limit.

**Fixed:** `MaximumLength` on all nine string members across the five validators — the heartbeat's four
entry columns (`ProcessName`, `WindowTitle`, plus `ProductName` and `ExecutablePath`, which had no rule
at all) and the three desktop / two android mapping patterns. The widths are constants in
`TrackingIngestLimits` beside the `SEC-5` caps rather than literals at nine call sites, so a column
widened in a migration has one place to be widened again. `ExecutablePath`'s column is unbounded — it is
an `EncryptedColumn`, so `text` holding ciphertext — and is bounded at 1024 anyway, because the value is
still client-supplied and stored for the account's lifetime.

**The index-tuple edge is a byte rule, not a length rule**, which is why it could not be folded into the
above: `WindowTitle` is `varchar(1024)` and the column accepts 1024 characters at any width, but 1024
three-byte characters is ~3 kB against Postgres' ~2704-byte index-tuple limit. `ProcessName` shares that
tuple, so the bound is on the pair — `Encoding.UTF8.GetByteCount` of both, capped at 2000 to leave room
for the three fixed columns and the tuple's overhead.

**Rejected rather than truncated**, against the finding's parenthetical. Two window titles cut to the
same prefix collide under `(UserId, WindowStart, RecordDate, ProcessName, WindowTitle)` and are then
*summed* by `CQ-8`'s `CollapseCollidingEntries` — so truncating would make a size limit silently merge
two distinct windows, which is the class of failure this module already spends the most effort avoiding.
A 400 costs the agent one minute's batch and says why.

Pinned by four cases in `TrackingValidationBoundsTests` — an over-long title, an over-long process name,
the byte edge (900 characters, inside the column and over the index limit, which asserts both of those
facts about its own fixture so the case cannot decay into a duplicate of the first), and an ordinary
entry that must still be a 200 — plus one per mapping endpoint.

`Doc impact: none` (`domain-map.md`'s navigation row for `TrackingIngestLimits` now mentions the widths;
no rule statement changed)

### CQ-10 🟠 A mapping's target cannot be changed, and update does not re-check the target rule

`application/dto/request/desktop/UpdateTrackerDesktopMappingRequest.cs:20-29`,
`application/dto/request/android/UpdateTrackerAndroidMappingRequest.cs:19-27`

`UpdateEntity` writes the pattern fields and `IsActive` — and nothing else. `IsIgnored`, `ActivityId`,
`RoleId` and `CategoryId` are not on the update DTO at all, so **re-pointing a rule from one activity to
another is impossible through the API**; the user has to delete and recreate it (losing nothing but
discovering it the hard way). Correspondingly the update validator omits the "exactly one target" and
"IsIgnored only true" rules its create twin carries — currently harmless, and a trap the moment
somebody adds the fields.

Given that `TrackerMappingActivityReferenceSource`'s whole remark is about how valuable a mapping is
("lose it and every future heartbeat matching that pattern silently stops being attributed"), making
users delete one to edit it is the wrong default.

**Fix:** add the four target members to both update DTOs and lift the create validator's two target
rules into a shared rule set applied to both.

`Doc impact: domain-map.md → Endpoints (the mappings rows say "create / update / delete"; worth a note
that update currently cannot change the target until this is fixed)`

### CQ-11 🟠 ✅ PARTLY MOOT / FIXED 2026-09-03 · Unique-pattern collisions surface as 500

`application/endpoint/desktop/command/CreateTrackerDesktopMappingEndpoint.cs`,
`UpdateTrackerDesktopMappingEndpoint.cs` (and the android pair)

The per-user unique pattern index — with `NULLS NOT DISTINCT`, so two rules that both leave
`ProcessName` null collide — is the most user-reachable constraint in the module: "map `code.exe` to
Programming" twice is an ordinary mistake. Neither endpoint nor validator checks for it, and
`BaseCreateEndpoint`/`BaseUpdateEndpoint` do not translate `23505`, so the user gets a 500.

**Fix:** pre-check the pattern tuple in the endpoint's validation hook and return a 409 (or a 400 naming
the field). Cheaper alternative: catch `DbUpdateException` with `PostgresException.SqlState == "23505"`
in the Framework base once, and every slice benefits.

**The premise is wrong on the mapping endpoints, and right on the ingest ones.** The finding says
`BaseCreateEndpoint`/`BaseUpdateEndpoint` do not translate `23505`. They do — both wrap `HandleAsync` in
`catch (Exception ex) { DbUtils.HandleException(ex, …) }`, and
`DatabaseExceptionUtils.cs:24-25` maps `PostgresErrorCodes.UniqueViolation` to
`ResultErrorType.DbUniqueViolationError`, which `EndpointHelper.ToStatusCode` sends as 409. The cheap
alternative was already in the Framework before this review, and
`RoutineTimePeriodUniqueIndexTests.Create_DuplicateText_Returns409` has been pinning it from another slice
all along. All four mapping endpoints derive from those bases, so **a duplicate pattern has always been a
409 here**, not a 500.

What was genuinely unhandled is the class the rest of this batch is about: the **three ingest endpoints**
are plain `Endpoint<>`s with no base and no try/catch, so a `23505` on any of them did reach the global
handler as a 500. That is the version of this finding that matters, because a desktop agent, a browser
extension and a phone all re-post a rejected batch unchanged.

**Fixed** with `application/endpoint/extensions/IngestBatchSaveExtensions.cs` — one `TrySaveIngestBatchAsync`
the three ingest saves go through, catching only `23505` and answering with
`EndpointHelper.ToStatusCode(ResultErrorType.DbUniqueViolationError)` rather than a literal 409, so the
status stays defined in the one place the CRUD bases already define it. Anything else still propagates: a
foreign-key or check violation on an ingest path is a bug in this slice, not something a client resolves by
retrying. It sits in Tracking rather than the Framework because `framework/` is a submodule and nothing
outside this slice has the shape yet (`CLAUDE.md`, "editing it is a two-repo operation"); if a second slice
grows unbased command endpoints, that is the moment to lift it.

The named fix — a pre-check returning a 409 that names the offending field — is **still open**. The user
gets "Unique constraint violation during Create" rather than "you already have a rule for `code.exe`".

Pinned by two cases in `TrackingIngestConflictTests`: the duplicate pattern, and the `NULLS NOT DISTINCT`
half — two rules that both name only a window title, leaving `ProcessName` and `ProductName` null, which
under Postgres' default null handling would *not* collide. That second one is the reachable surprise in this
finding and had no guard anywhere.

`Doc impact: domain-map.md → Invariants + Navigation index (the ingest-save wrapper)` — done.

### CQ-12 🟠 `PatternMatchType.Regex` means two different things on two surfaces

`application/endpoint/desktop/command/DesktopActivityHeartbeatEndpoint.cs:150` vs
`framework/Sydowwe.Framework/infrastructure/persistence/QueryableEntityExtensions.cs:170`

At ingest, `Regex` runs `Regex.IsMatch(value, pattern)`. In every grid filter that takes the same enum
(`FetchTableDistinctDesktopEntry`, `FetchCategoriesAndRolesByPattern`, both mapping grids,
`FetchTableDistinctAndroidEntry`), `ApplyStringMatchFilter` maps `PatternMatchType.Regex` to
`EF.Functions.Like(field, value)` — a **SQL LIKE with the regex as the pattern**. `.*` matches nothing;
`%` matches everything.

So a user who writes a regex on a mapping rule and then types the same string into the settings grid to
find which entries it matches gets an unrelated answer, with no error on either side. The helper is
Framework-owned but this slice is its only consumer of the `Regex` member.

**Fix (framework):** translate `Regex` to Npgsql's `~` operator
(`EF.Functions.Matches`/`NpgsqlDbFunctionsExtensions`) or drop the member from the filter surface and
reject it in the grid validators. Either is better than the silent reinterpretation.

`Doc impact: none`

---

## 9. Performance hotspots

| Where | Issue | Impact | Mitigation | ID |
|---|---|---|---|---|
| All 15 per-source dashboards, e.g. `DesktopPieChartEndpoint.cs:38-43`, `WebExtensionSummaryCardsEndpoint.cs:91-94` | Every endpoint `ToListAsync()`es the **whole envelope of raw ledger rows** and groups/sums in memory | A 366-day span of per-minute rows is ~500 k entities per source; hundreds of MB of tracked-entity graph per request, GC pressure, and a wire-idle Postgres | Project to the aggregate in SQL (`GroupBy` + `Sum` translate fine for the pie/summary/stacked shapes); keep the in-memory path only where the session algorithm genuinely needs rows (timelines, focus-metrics). Add `AsNoTracking()` everywhere in the meantime — none of these 15 reads needs the change tracker | `PERF-1` |
| `AndroidStackedBarsEndpoint.cs:63-86` | `foreach (bucket) { sessions.Where(overlap) }` — a linear scan of every session per bucket | 366 days × 96 buckets ≈ 35 k buckets; × 10 k sessions ≈ 350 M comparisons in one request. The other two stacked-bars use `DailyWindowSet.BucketIndexOf`'s binary search | Sort sessions once and sweep, or index sessions by bucket via `BucketIndexOf` on their clamped ranges. **The read** that feeds it got `(user_id, session_end_utc)` on 2026-09-03 (see below); the bucket scan itself is untouched and is the larger half | `PERF-2` |
| ~~`application/validator/desktop/DesktopProcessDetailsValidator.cs`~~ **✅ FIXED 2026-09-03** | `To > From` and **no maximum range** — its web-extension twin capped at 31 days | An unbounded read of the partitioned desktop ledger filtered only by process name; no index supports it either (see below) | Fixed as the 31-day cap, hoisted rather than copied: `DashboardDateRangeRules.MaxDetailsSpanDays` + `ApplyDetailsDateRangeRules`, which both details validators now call for the whole envelope (`From`/`To`/span/daily-window mask). A second literal `31` in the twin is how this drifted the first time. `DesktopProcessDetailsRequest.ProcessName` also gained the column-width bound `CQ-9` added everywhere else. The `(user_id, process_name, window_start)` index it wanted shipped 2026-09-03 with the other two (see below) | `PERF-3` |
| `FetchTableDistinctDesktopEntry.cs:44-57`, `FetchTableDistinctAndroidEntry.cs:44-56` | `GROUP BY` over the **entire** ledger with no date predicate, executed twice per page (count + page) | A full partitioned-table scan per grid keystroke; grows without bound because nothing but the purge ever removes rows | Bound the grid to a date range (the settings UI already knows one), or materialise a distinct-entries view refreshed off-request | `PERF-4` |
| `DesktopPieChartEndpoint.cs:58-62`, `WebExtensionPieChartEndpoint.cs:58-61` | `WindowTitles` / `Pages` are `Distinct()` over the whole span, unbounded, per item | A year of browsing is tens of thousands of distinct URLs serialised into a pie-chart response nobody renders | Cap to the top N by seconds (the details endpoints exist for the full list) | `PERF-5` |
| ~~Three summary-cards `AllTime` baselines~~ **✅ FIXED 2026-09-03** | Read the entire ledger from the beginning of time. Desktop and android have no partition-date floor (only `WebExtensionActivityEntry` does, `AppDbContext.cs:279`) | Would be the heaviest query in the app — it threw first (`CQ-1`), which is the only reason nobody had noticed | Fixed with `CQ-1` in the shared base: the lookback is floored at `DateFrom - MaxSpanDays`, so the worst `AllTime` read is now a year and a day. The envelope read itself is still row-by-row (`PERF-1`, open) | `PERF-6` |
| `DesktopActivityHeartbeatEndpoint.cs:115-119` | One `TotalSecondsOnDayAsync` round trip per attributed activity, after the save, on every heartbeat | Small (few activities per window) but it is on the once-a-minute-per-user path | Batch the day totals into one seam call | `PERF-7` |
| `TimelineSegmentBuilder.AbsorbContextSwitches:196-241` | `do { … } while (merged)` restarts the whole scan after every single merge | O(n²) on a day with many short runs (n ≈ 1440); fine today, quadratic by construction | Single pass with a rebuilt list, or restart at `i` rather than at 0 | `PERF-8` |

### Indexes that should exist — ✅ ALL THREE SHIPPED 2026-09-03

**One migration, `20260903003933_TrackingLedgerReadIndexes`**, rather than one per finding. The three
were raised by `SEC-3`, `PERF-2` and `PERF-3` independently, but they are the same operation on the same
two tables: three migrations would have meant three deploys and three rollout conversations for one
maintenance window. The `HasIndex` calls live with their entities
(`AndroidSessionDataConfiguration`, `DesktopActivityEntryConfiguration`), each carrying a comment naming
the read it serves, so a later reader can tell an index that earns its write cost from one nobody
remembers.

- **`desktop_activity_entry (user_id, process_name, window_start)`** — `DesktopProcessDetailsEndpoint.cs:32-35`
  filters exactly on that triple and today rides `(user_id, window_start)` then filters, reading every
  process to return one. `PERF-3` bounds it at 31 days as of 2026-09-03, so it is no longer the module's
  worst read — but the index is still the thing that would make it cheap, and this is rollout work
  (`MIG-4`'s neighbourhood) rather than part of the validator fix. **Shipped.** This is the one with a
  rollout cost: `desktop_activity_entry` is RANGE-partitioned, so `CREATE INDEX` propagates to all six
  partitions under `ACCESS EXCLUSIVE` and `CONCURRENTLY` is not available on a partitioned parent. That is
  `MIG-2`'s risk, incurred deliberately — see the note there for the per-partition escape hatch.
- **`android_session_data (user_id, session_end_utc)`** — `AndroidStackedBarsEndpoint.cs:41` predicates on
  `SessionStartUtc < envelopeTo && SessionEndUtc > envelopeFrom`; only the start side is indexed, so the
  overlap read degenerates to a scan of everything before the envelope's end. **Shipped.** It does not
  make `PERF-2`'s in-memory bucket scan any less quadratic — that is a separate, still-open fix; it only
  stops the read that feeds it from scanning.
- **`android_session_data (session_start_utc)`** — the retention sweep `SEC-3` added. Deliberately *not*
  user-scoped: the purge has no `user_id` predicate, so the existing `(user_id, session_start_utc)` cannot
  serve it. **Shipped.**
- **`web_extension_activity_entry (user_id, window_start, domain)`** already exists as the unique index and
  covers `CQ-5`'s batched lookup — rewritten 2026-09-03, no new index needed. The desktop equivalent
  (`CQ-8`'s already-stored read) rides the existing `(user_id, window_start)` index the same way.
- No index is needed for the purge on the *partitioned* tables: both are RANGE-partitioned on
  `record_date`, so the predicate prunes whole partitions.

No test pins these. `PostgresContainerFixture` builds the schema with `EnsureCreated`, so the suite proves
the indexes are *creatable* (including on the partitioned parent, which rejects some shapes) but nothing
asserts a plan uses them — an index is a performance fact, and this suite has no vehicle for those.

---

## Code/doc drift

### DOC-1 🟠 ✅ MOOT 2026-09-03 · `domain-map.md` states an auth rule the web-extension heartbeat does not follow

`docs/domain-map.md:189-191` — *"the three ingest endpoints carry `[AllowExtensionClients]` **and**
`Policies(PortalAuthorizationPolicies.ActivityTracking)`"*. Two of the three do. **The code is wrong**
(`SEC-1`); the sentence is the rule worth keeping.

`Doc impact: none once SEC-1 is fixed`

### DOC-2 🟠 ✅ MOOT 2026-09-03 · The focus-metrics baseline rule claims parity with summary-cards that does not exist

`docs/domain-map.md:138-140` says the two baselines scale the same way and must not disagree. The
per-source summary-cards baselines do not apply the daily window at all (`CQ-2`), so on any narrowed
window the two dashboards on one screen disagree by the size of the excluded night. **The code is
wrong**; fixing `CQ-2` makes the sentence true.

**Closed with `CQ-2`, with no doc edit** — the sentence at `domain-map.md:161-163` is now a description
of what the code does. The parity is structural rather than transcribed: the per-source cards and
`BaseFocusMetricsEndpoint` derive their lookback the same way because they are two bases written to the
same shape, which is as close as this gets without merging measures that genuinely differ.

`Doc impact: none`

### DOC-3 🟠 ✅ FIXED 2026-09-03 · Retention is described as the slice's GDPR control while one of the three ledgers is outside it

`docs/summary.md:35-37` and `docs/domain-map.md:227` describe the purge as *the* retention mechanism for
the slice, naming the two partitioned tables. Both statements are literally accurate, and together they
let `AndroidSessionData` fall between them — it is listed as a ledger in the Entities table with no
retention column at all. **Both sides need a change**: add the purge (`SEC-3`) and say so in the two
docs.

**Fixed:** both sides. The purge covers `AndroidSessionData` (`SEC-3`), and the two docs now say which
column each of the three ledgers ages on — plus the `AndroidSessionData` row in `domain-map.md`'s Entities
table, which was the place the omission was actually visible.

`Doc impact: done` — summary.md → Bounded context (Retention); domain-map.md → Infrastructure + Entities

### DOC-4 🟡 `Program.cs`'s assembly-list comment says 29 endpoints; there are 38

`AdhdTimeOrganizer/Program.cs:257` — *"the 29 ingest / pattern-mapping / dashboard endpoints"*.
`docs/summary.md:25` says 38, and 38 is correct (14 desktop + 7 web-extension + 11 android + 6 unified).
The host comment predates the six unified routes. **Fix the host comment.**

`Doc impact: none` (host-side comment, not a module doc)

### DOC-5 🟠 `docs/testing.md` credits a partition guard that does not exist

`docs/testing.md:176` says `TrackingRouteSmokeTests` covers silent failures 1, 4 **and 5** — #5 being
"Tracking's `ApplyConfigurationsFromAssembly` missing from `AppDbContext` → the two partitioned tables
quietly become plain tables in the next migration". The class's own XML doc
(`TrackingRouteSmokeTests.cs:35-37`) `<see cref>`s a `PartitionedTrackingTables_KeepTheirPartitionKey`
method. **The file contains no such test.** Its five methods are the two route theories,
`DesktopDistinctProcessesRoute_IsRegistered`, `WebExtensionActivityEntry_KeepsBothOfItsQueryFilters` and
`RetentionPurgeJob_IsRegisteredAndScheduled`.

So the loudest-documented silent failure in the module has no guard, and the docs say it does — the
worst of the two possible errors. **Both sides need a change**: write the test (`TEST-8`) and fix the
stale `<see cref>`s.

`Doc impact: testing.md → The guards (add the row once the test exists)`

### DOC-6 🟡 `summary.md` describes the `WebExtensionActivityEntry` date filter as a partition bound; it is a 2-year retention floor

`docs/summary.md:148-150` calls losing the date half "unbounds the partition read".
`AppDbContext.cs:41` defines `CurrentPartitionDate` as `today.AddYears(-2)` — which is a *retention*
horizon, not a partition boundary (the partitions are yearly, 2026-2030 plus a default). The practical
consequence is worth stating plainly in the doc, because it is surprising: **the web-extension
dashboards cannot see data older than two years even when the ledger still holds it**, so an `AllTime`
baseline means something different on that source than on the other two. **The doc should be
sharpened**; the code is deliberate.

`Doc impact: summary.md → Things that fail silently (item 4); domain-map.md → Entities (WebExtensionActivityEntry note)`

---

## Appendix — 🟡 nits

- **CQ-13** The two hand-written distinct-entry grids and `FetchCategoriesAndRolesByPattern` wrap the
  whole handler in `try { … } catch (Exception ex) { … 500 }`
  (`FetchTableDistinctDesktopEntry.cs:34-72`, `FetchTableDistinctAndroidEntry.cs:35-71`,
  `FetchCategoriesAndRolesByPattern.cs:34-77`). No other endpoint in the slice does; it also swallows
  `OperationCanceledException`, turning a client disconnect into a logged error. Delete the catch and let
  the global handler answer. `Doc impact: none`
- **CQ-14** ✅ **FIXED 2026-09-03.** Those same two grids had **no validator**, while the two mapping
  grids validate `ItemsPerPage ∈ [1,200]`. `ItemsPerPage = 0` gives `(int)Math.Ceiling(n / 0.0)` → a
  garbage `pageCount`; a large value is an unbounded page.
  `FetchTableDistinct{Desktop,Android}EntryValidator` mirror `FetchTableTrackerDesktopMappingValidator`
  as the finding says, including its "a filter value needs its match type" rules — the two handlers apply
  a filter only when both are set, so a value sent without one was silently ignored rather than filtered
  on. Both are wired with an explicit `Validator<…>()` in `Configure()` the way every other grid here is,
  rather than left to FastEndpoints' discovery: these bind
  `BaseFilterSortPaginateRequest<TFilter>`, and a validator picked up by request type alone would be a
  silent thing to lose. Pinned by `TrackingValidationBoundsTests.DistinctEntriesGrid_RejectsAPageSizeOutsideItsBounds`,
  a theory over both routes covering zero, 10 000 and an ordinary page. `Doc impact: none`
- **CQ-15** `GridTrackerDesktopMappingEndpoint.cs:32` / `GridTrackerAndroidMappingEndpoint.cs:32` throw
  `ArgumentOutOfRangeException` for an unmapped `Type` → 500 rather than 400. Two smaller oddities in the
  same switch: the filter's `Type` is `required`, so **"all mappings, unfiltered" is unrepresentable**;
  and the android grid switches on `TrackerDesktopMappingTypeEnum` — correct by value, misleading by
  name. `Doc impact: domain-map.md → Entities (the enum note says "the mapping's target shape"; worth
  saying it is shared by both tables)`
- **CQ-16** `DesktopActivityHeartbeatEndpoint.cs:64` takes `mappings.FirstOrDefault(m => MatchesPattern(...))`
  with no ordering — which rule wins when two match is whatever order Postgres returned. A specificity
  order (most pattern columns set first, then id) would make it deterministic and explicable.
  `Doc impact: domain-map.md → Invariants (state which mapping wins)`
- **CQ-17** `DesktopPieChartEndpoint.cs:101` counts `x.WindowTitle != null` on a non-nullable column
  (always true) while the list three lines above uses `!string.IsNullOrEmpty`. The totals row therefore
  counts `""` as a distinct title. `Doc impact: none`
- **CQ-18** ✅ **FIXED 2026-09-03.** The baseline block was transcribed **four** times — three per-source
  summary-cards plus `BaseFocusMetricsEndpoint` — with `UnifiedSummaryCardsEndpoint` a fifth variant.
  `CQ-1`, `CQ-2` and `CQ-3` were each a copy that drifted; the module elsewhere (`TimelineSegmentBuilder`,
  `UnifiedMinuteMerger`, `DashboardDateRangeRules`) is explicit that one copy is the rule.

  **Fixed as written:** `application/endpoint/BaseSummaryCardsEndpoint.cs`, in the shape of
  `BaseFocusMetricsEndpoint` — the one of the five that already got all three right. The three per-source
  endpoints lost ~120 lines each and now supply exactly two things: `LoadAsync`, their ledger read
  projected into a shared `SummaryRow`, and `BuildItem`, their DTO. Everything between those two — the
  lookback arithmetic, the per-day window applied to the lookback (`CQ-2`), the weekday and day-span fold
  on the user's calendar (`CQ-3`), the `AllTime` floor (`CQ-1`, `PERF-6`) and the scale-to-span — exists
  once. `DOC-2` closes with it.

  Three deliberate details:

  - **`TRequest, TItem`, not `TRow`.** Sources project into `SummaryRow(Key, Detail, Instant, Active,
    Background)` after their own read, so the entity type never reaches the base and the merged card
    could adopt it later without a third parameter. `Detail` is the secondary label a source carries
    (desktop's product name, android's package name) and is null where there is none.
  - **Android's duration lands in `ActiveSeconds` with a zero background**, which is what that ledger
    means — the merged card already says so ("background is always null for an item only android saw").
    It is why the android DTO has one stat where its siblings have two.
  - **`LoadAsync` is called twice per request**, once per window set, rather than the span being loaded
    and the lookback derived from it. Two reads is what the three endpoints already did; deriving one
    from the other would need the span read to cover the lookback, which is the unbounded read `CQ-1`
    was about.

  Behaviour is otherwise unchanged, including the per-source default `topN` (5/5/4 — `CQ-23` is still
  open, and folding it in here would have hidden a UI change inside a refactor). Ties in the top-N order
  now break on the key rather than on whatever order Postgres returned.

  Pinned by `TrackingSummaryCardsBaselineTests` (5 cases across the three routes), which asserts on the
  averages the cards print — all three bugs returned a well-formed 200.
  `Doc impact: domain-map.md → Navigation index (new base row)` — done.
- **CQ-19** `AndroidSessionData` carries no `[AuditIgnore]` on `PackageName` / `AppLabel` while both
  other ledgers annotate their sensitive columns. See `AUDIT-2` in
  [`03-risks-rollout.md`](03-risks-rollout.md). `Doc impact: none`
- **CQ-20** ✅ **FIXED 2026-09-03.** `DesktopActivityHeartbeatValidator` chained
  `.Must(…).Must(…).WithMessage(…)` — the message attaches only to the second rule, so the per-entry
  60-second cap failed with FluentValidation's default text. Also the message ("Total seconds across all
  entries") described the sum of *active* seconds only. Now two separate `RuleFor`s, each with its own
  message, and the sum's says "Total active seconds". Split rather than given a shared message: they are
  two different bounds, and one of them names the offending entry's shape while the other names the
  batch's. Pinned by
  `TrackingValidationBoundsTests.DesktopHeartbeat_NamesThePerEntryCapItFailedRatherThanTheDefaultText`,
  which asserts on the response body — the old chain returned a 400 too, just an unactionable one.
  `Doc impact: none`
- **CQ-21** `WebExtensionEntryDto.Url` is `required string` while the entity's `Url` is `string?` and the
  handler branches on `string.IsNullOrEmpty` — the DTO should be `string?` to match the documented
  "domain-only tracking is legitimate" case (`WebExtensionActivityEntry.cs:42-45`). `Doc impact: none`
- **CQ-22** ✅ **FIXED 2026-09-03.** `AndroidSyncEndpoint` logged client-supplied strings
  (`item.SessionStartUtc`, `item.SessionEndUtc`) at Warning. Package names and timestamps are not PII under
  the CLAUDE.md rule, but these are unvalidated free text on a per-request path. Bounded in
  `AndroidSyncValidator` via `TrackingIngestLimits.MaxInstantStringLength` (64 — an ISO-8601 instant with an
  offset is 33), rather than truncating at the log site: the string is then bounded for every consumer, not
  just the one line that logs it. `PackageName` and `AppLabel` were already capped at 255. This one is a
  400 for the whole sync rather than a dropped row — the split `SEC-5` set is "a row a phone could
  plausibly produce" versus "a broken or hostile client", and a multi-kilobyte timestamp is the latter.
  Pinned by `AndroidSyncSessionStorageTests.AndroidSync_RejectsAnInstantStringTooLongToLog`.
  `Doc impact: none`
- **CQ-23** `AndroidSummaryCardsEndpoint.cs:51` defaults `TopN` to 4 while the desktop and web-extension
  cards default to 5, and `UnifiedSummaryCardsEndpoint.cs:33` to 4. Harmless, but it is the sort of
  inconsistency that reaches the screen. `Doc impact: none`
