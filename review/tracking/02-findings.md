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

### SEC-2 🟠 Mapping create accepts another user's `activityId` / `roleId` / `categoryId`

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

`Doc impact: domain-map.md → Invariants (add "a mapping's activity/role/category is the author's own",
app-enforced)`

### SEC-3 🟠 `AndroidSessionData` is never purged — the retention job covers two of the three ledgers

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
  a migration on the host and is rollout work, not part of the code fix.
- **A test.** Nothing in the suite *executes* this handler — `RetentionPurgeJob_IsRegisteredAndScheduled`
  only asserts it was registered — so the new predicate has no row-level guard, exactly like the two that
  preceded it. `AUDIT-1`'s "log the run unconditionally" is untouched for the same reason.

`Doc impact: done` — `summary.md` → Bounded context (the Retention bullet now names all three ledgers and
which column each ages on); `domain-map.md` → Infrastructure (retention row) and the `AndroidSessionData`
entity row, which now says it is the one unpartitioned ledger. `DOC-3` closes with this.

### SEC-4 🟠 User-authored regexes run un-timed on the ingest thread

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

`Doc impact: summary.md → Gotchas (the "matched in memory, not in SQL" bullet gains the timeout)`

### SEC-5 🟠 No bound on ingest batch size or android session length

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

`Doc impact: domain-map.md → Invariants (an android session is at most one day and is not in the future)`

---

## 6. Code quality

### CQ-1 🟠 `BaselineType.AllTime` throws in production and cannot throw in tests

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

`Doc impact: none`

### CQ-2 🟠 The summary-cards baseline ignores the per-day time-of-day window

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

`Doc impact: none` (the doc states the intended rule; the code will then match it)

### CQ-3 🟠 The android summary cards fold the weekday and the day span on UTC

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

### CQ-5 🟠 The web-extension heartbeat is an N+1 read with a lost-update race

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

`Doc impact: none`

### CQ-6 🟠 Android sync re-labels an offset-bearing timestamp as UTC instead of converting it

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

`Doc impact: none`

### CQ-7 🟠 Android sync does not de-duplicate within a batch

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

`Doc impact: none`

### CQ-8 🟠 The desktop heartbeat can violate its own unique index inside one window

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

`Doc impact: none`

### CQ-9 🟠 No length validation anywhere on the write paths

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

`Doc impact: none`

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

### CQ-11 🟠 Unique-pattern collisions surface as 500

`application/endpoint/desktop/command/CreateTrackerDesktopMappingEndpoint.cs`,
`UpdateTrackerDesktopMappingEndpoint.cs` (and the android pair)

The per-user unique pattern index — with `NULLS NOT DISTINCT`, so two rules that both leave
`ProcessName` null collide — is the most user-reachable constraint in the module: "map `code.exe` to
Programming" twice is an ordinary mistake. Neither endpoint nor validator checks for it, and
`BaseCreateEndpoint`/`BaseUpdateEndpoint` do not translate `23505`, so the user gets a 500.

**Fix:** pre-check the pattern tuple in the endpoint's validation hook and return a 409 (or a 400 naming
the field). Cheaper alternative: catch `DbUpdateException` with `PostgresException.SqlState == "23505"`
in the Framework base once, and every slice benefits.

`Doc impact: none`

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
| `AndroidStackedBarsEndpoint.cs:63-86` | `foreach (bucket) { sessions.Where(overlap) }` — a linear scan of every session per bucket | 366 days × 96 buckets ≈ 35 k buckets; × 10 k sessions ≈ 350 M comparisons in one request. The other two stacked-bars use `DailyWindowSet.BucketIndexOf`'s binary search | Sort sessions once and sweep, or index sessions by bucket via `BucketIndexOf` on their clamped ranges | `PERF-2` |
| `application/validator/desktop/DesktopProcessDetailsValidator.cs` | `To > From` and **no maximum range** — its web-extension twin caps at 31 days (`DomainDetailsValidator.cs:13`) | An unbounded read of the partitioned desktop ledger filtered only by process name; no index supports it either (see below) | Add the same 31-day cap, or reuse `DashboardDateRangeRules.MaxSpanDays` | `PERF-3` |
| `FetchTableDistinctDesktopEntry.cs:44-57`, `FetchTableDistinctAndroidEntry.cs:44-56` | `GROUP BY` over the **entire** ledger with no date predicate, executed twice per page (count + page) | A full partitioned-table scan per grid keystroke; grows without bound because nothing but the purge ever removes rows | Bound the grid to a date range (the settings UI already knows one), or materialise a distinct-entries view refreshed off-request | `PERF-4` |
| `DesktopPieChartEndpoint.cs:58-62`, `WebExtensionPieChartEndpoint.cs:58-61` | `WindowTitles` / `Pages` are `Distinct()` over the whole span, unbounded, per item | A year of browsing is tens of thousands of distinct URLs serialised into a pie-chart response nobody renders | Cap to the top N by seconds (the details endpoints exist for the full list) | `PERF-5` |
| Three summary-cards `AllTime` baselines (`DesktopSummaryCardsEndpoint.cs:97` etc.) | Reads the entire ledger from the beginning of time. Desktop and android have no partition-date floor (only `WebExtensionActivityEntry` does, `AppDbContext.cs:279`) | Would be the heaviest query in the app — it currently throws first (`CQ-1`), which is the only reason nobody has noticed | Same fix as `CQ-1`: floor at `MaxSpanDays` | `PERF-6` |
| `DesktopActivityHeartbeatEndpoint.cs:115-119` | One `TotalSecondsOnDayAsync` round trip per attributed activity, after the save, on every heartbeat | Small (few activities per window) but it is on the once-a-minute-per-user path | Batch the day totals into one seam call | `PERF-7` |
| `TimelineSegmentBuilder.AbsorbContextSwitches:196-241` | `do { … } while (merged)` restarts the whole scan after every single merge | O(n²) on a day with many short runs (n ≈ 1440); fine today, quadratic by construction | Single pass with a rebuilt list, or restart at `i` rather than at 0 | `PERF-8` |

### Indexes that should exist

- **`desktop_activity_entry (user_id, process_name, window_start)`** — `DesktopProcessDetailsEndpoint.cs:32-35`
  filters exactly on that triple and today rides `(user_id, window_start)` then filters, reading every
  process to return one. With `PERF-3` unfixed it is the module's worst read.
- **`android_session_data (user_id, session_end_utc)`** — `AndroidStackedBarsEndpoint.cs:41` predicates on
  `SessionStartUtc < envelopeTo && SessionEndUtc > envelopeFrom`; only the start side is indexed, so the
  overlap read degenerates to a scan of everything before the envelope's end.
- **`web_extension_activity_entry (user_id, window_start, domain)`** already exists as the unique index and
  covers `CQ-5`'s batched lookup once that is rewritten — no new index needed there.
- No index is needed for the purge: both purged tables are RANGE-partitioned on `record_date`, so the
  predicate prunes whole partitions. Adding android to the purge (`SEC-3`) **does** need one — its table
  is not partitioned; `(user_id, session_start_utc)` exists and a plain `session_start_utc` index would
  serve the cross-user sweep better.

---

## Code/doc drift

### DOC-1 🟠 ✅ MOOT 2026-09-03 · `domain-map.md` states an auth rule the web-extension heartbeat does not follow

`docs/domain-map.md:189-191` — *"the three ingest endpoints carry `[AllowExtensionClients]` **and**
`Policies(PortalAuthorizationPolicies.ActivityTracking)`"*. Two of the three do. **The code is wrong**
(`SEC-1`); the sentence is the rule worth keeping.

`Doc impact: none once SEC-1 is fixed`

### DOC-2 🟠 The focus-metrics baseline rule claims parity with summary-cards that does not exist

`docs/domain-map.md:138-140` says the two baselines scale the same way and must not disagree. The
per-source summary-cards baselines do not apply the daily window at all (`CQ-2`), so on any narrowed
window the two dashboards on one screen disagree by the size of the excluded night. **The code is
wrong**; fixing `CQ-2` makes the sentence true.

`Doc impact: none once CQ-2 is fixed`

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
- **CQ-14** Those same two grids have **no validator**, while the two mapping grids validate
  `ItemsPerPage ∈ [1,200]`. `ItemsPerPage = 0` gives `(int)Math.Ceiling(n / 0.0)` → a garbage
  `pageCount`; a large value is an unbounded page. Add a validator mirroring
  `FetchTableTrackerDesktopMappingValidator`. `Doc impact: none`
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
- **CQ-18** The baseline block is transcribed **four** times — three per-source summary-cards plus
  `BaseFocusMetricsEndpoint` — with `UnifiedSummaryCardsEndpoint` a fifth variant. `CQ-1`, `CQ-2` and
  `CQ-3` are each a copy that drifted; the module elsewhere (`TimelineSegmentBuilder`,
  `UnifiedMinuteMerger`, `DashboardDateRangeRules`) is explicit that one copy is the rule. A
  `BaseSummaryCardsEndpoint<TRequest, TItem>` in the shape of `BaseFocusMetricsEndpoint` would collapse
  all three findings into one. `Doc impact: domain-map.md → Navigation index (new base row)`
- **CQ-19** `AndroidSessionData` carries no `[AuditIgnore]` on `PackageName` / `AppLabel` while both
  other ledgers annotate their sensitive columns. See `AUDIT-2` in
  [`03-risks-rollout.md`](03-risks-rollout.md). `Doc impact: none`
- **CQ-20** `DesktopActivityHeartbeatValidator.cs:18-22` chains `.Must(…).Must(…).WithMessage(…)` — the
  message attaches only to the second rule, so the per-entry 60-second cap fails with FluentValidation's
  default text. Also the message ("Total seconds across all entries") describes the sum of *active*
  seconds only. `Doc impact: none`
- **CQ-21** `WebExtensionEntryDto.Url` is `required string` while the entity's `Url` is `string?` and the
  handler branches on `string.IsNullOrEmpty` — the DTO should be `string?` to match the documented
  "domain-only tracking is legitimate" case (`WebExtensionActivityEntry.cs:42-45`). `Doc impact: none`
- **CQ-22** `AndroidSyncEndpoint.cs:40,50,57` logs client-supplied strings (`item.SessionStartUtc`,
  `item.SessionEndUtc`) at Warning. Package names and timestamps are not PII under the CLAUDE.md rule,
  but these are unvalidated free text on a per-request path — bound the length before logging.
  `Doc impact: none`
- **CQ-23** `AndroidSummaryCardsEndpoint.cs:51` defaults `TopN` to 4 while the desktop and web-extension
  cards default to 5, and `UnifiedSummaryCardsEndpoint.cs:33` to 4. Harmless, but it is the sort of
  inconsistency that reaches the screen. `Doc impact: none`
