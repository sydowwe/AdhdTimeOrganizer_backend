# Portal review — 02 · Findings

> ⚠ **Scope:** 42 of 712 portal files (~6%). No endpoint, validator or DTO was reviewed. Absence of
> a finding in those layers means *not looked at*, not *clean*. See `00-STATUS.md`.

**Severity:** 🔴 blocker (legal / security / data-corruption) · 🟠 important (wrong behavior or
maintainability hole) · 🟡 polish. 🟡 items are collected in the Nits appendix except where they
carry security weight.

Line numbers come from the per-file fragments in `findings/` and were not re-verified against the
source. `Confidence` is carried through where the reviewing agent flagged it as Low.

---

## 5. Security & authorization

### SEC-1 🔴 Plaintext passwords and PII persisted to the log database
`AdhdTimeOrganizer/Program.cs:409-419` + `config/SerilogConfig.cs:28`

`UseSerilogRequestLogging`'s `EnrichDiagnosticContext` buffers up to 1000 chars of the **raw request
body for every non-GET request** and sets it as a Serilog property. `SerilogConfig`'s
`PropertiesColumnWriter(NpgsqlDbType.Jsonb)` then serializes *every* property into the `properties`
column of the Postgres sink, which is active in Production. Login, register and change-password
bodies contain **plaintext passwords**; every other write body carries emails and names.

`PiiRedactor` exists in the framework but is not wired into this pipeline — exactly the gap
CLAUDE.md's "Logging (no PII at the call site)" section warns about. Compounding it, the sink's
`user_agent` / `client_ip` / `auth_method` / `user_id` / `role` columns are **commented out**, which
reads as "these were deliberately excluded" — but `PropertiesColumnWriter` still writes all of them
into the JSONB blob. Anyone auditing this file would draw the wrong conclusion.

Credentials at rest in a queryable table, with no retention (`AUDIT-3`), is a GDPR Art. 5/32
exposure and a credential-leak amplifier for any log-DB compromise.

**Fix:** stop logging request bodies, or allow-list non-auth routes and strip the property before
the sink. If `PropertiesColumnWriter` stays, allow-list which diagnostic keys are persisted rather
than writing everything.
**Doc impact:** none. (CLAUDE.md already states the rule this violates.)

### SEC-2 🟠 Google Calendar OAuth refresh token stored in plaintext
`domain/model/entity/user/User.cs:21`, configured `infrastructure/persistence/configuration/user/UserEntityConfiguration.cs:16`

`GoogleCalendarRefreshToken` is a `varchar(500)` plain column. A refresh token is a **long-lived**
credential granting standing read/write access to the user's Google Calendar — it does not expire
like an access token, so a backup leak, SQL-injection read, reporting replica or insider access
yields durable access to a third-party account, not just app-internal state.

This is the designated use case for `EncryptedColumn` (AES-256-GCM, `FIELD_ENCRYPTION_KEY`), which
CLAUDE.md notes is "currently unused by any entity". The column is only ever read by user id
(`SyncCalendarToGoogleEndpoint`, `ConnectGoogleCalendarEndpoint`), so the non-filterable/non-sortable
constraint costs nothing here.

**Fix:** `builder.EncryptedColumn(u => u.GoogleCalendarRefreshToken)` + migration (see `MIG-1`).
`GoogleOAuthUserId` is lower-sensitivity but is the other half of a full de-anonymization if leaked.
**Doc impact:** `domain-map.md` → Invariants (add an at-rest-encryption note for the user OAuth fields).

### SEC-3 🟠 Every SQL statement logged to console in Production — twice
`Program.cs:128` and `infrastructure/persistence/AppDbContext.cs:157-160`

Two independent, unconditional `LogTo(Console.WriteLine)` registrations: one in the `AddDbContext`
callback, one in an `OnConfiguring` override. Because options arrive via DI, the second is
**additive, not a replacement** — every SQL command is written to stdout twice, at `Information`
level, in every environment including Production, bypassing Serilog entirely.

Npgsql parameter values reach these logs, and `DatabaseStringsHelper` hardcodes
`Include Error Detail=true` (`SEC-6`), so query text plus user data lands in whatever aggregator
scrapes stdout. Neither path is redacted.

**Fix:** delete the `OnConfiguring` override entirely (Program.cs already configures logging) and
gate the remaining `LogTo` behind `IsDevelopment()`.
**Doc impact:** none.

### SEC-4 🟠 Desktop window titles stored in plaintext with no retention
`domain/model/entity/activityTracking/desktop/DesktopActivityEntry.cs:12`

`WindowTitle` and `ExecutablePath` routinely contain document names, chat and email subject lines,
and URLs — among the most sensitive free-text PII this application handles. Stored as plain strings,
no `EncryptedColumn`, no `[AuditIgnore]`, and **no retention purge job exists** (searched; none
found), unlike the Scheduler/Reminders/Notifications ledgers CLAUDE.md documents.

Note the encryption fix is **not** mechanical: `DesktopActivityEntryConfiguration.cs:31` builds a
unique index over `WindowTitle` for ingest idempotency, and `EncryptedColumn` is randomized and
therefore cannot be indexed. See `MIG-2`.

**Fix:** retention purge first (unblocked, high value); encryption second, via a separate
non-encrypted hash column carrying the uniqueness constraint. `[AuditIgnore]` regardless.
**Doc impact:** `domain-map.md` → Business rules → Time tracking (state the retention window).

### SEC-5 🟠 Browsing history retained indefinitely
`domain/model/entity/activityTracking/WebExtensionActivityEntry.cs:9-10`

`Domain` and `Url` (up to 2048 chars) are the user's browsing history. No purge job exists. The
entity's query filter (`RecordDate >= CurrentPartitionDate`) **hides** old rows from EF reads but
deletes nothing — raw SQL, reporting and the partitions themselves still hold years of URL history.
A filter that looks like retention but isn't is worse than no filter, because it suppresses the
symptom that would prompt someone to add a real purge.

**Fix:** `RetentionOptions` subclass + purge handler modeled on `PurgeExpiredRunLogsJobHandler`;
drop whole partitions where possible. `[AuditIgnore]` on `Url`.
**Doc impact:** `domain-map.md` → Business rules → Time tracking (retention window).

### SEC-6 🟡 `Include Error Detail=true` puts parameter values in exception messages
`config/DatabaseStringsHelper.cs:7,10`

Both connection strings inherit this from `Helper.GetDatabaseConnectionString`. Npgsql then embeds
actual parameter values in exception messages, which — given `SEC-1`/`SEC-3` and no redaction — flow
straight into logs. **Fix:** gate behind `IsDevelopment()`. **Doc impact:** none.

### SEC-7 🟡 `WebExtensionActivityEntry` filter ignores the `UserScoping:Enabled` switch
`infrastructure/persistence/AppDbContext.cs:141-149`

The hand-written filter is gated only on `loggedUserService != null`, not on `UserScopingOptions.Enabled`
— the switch every other `IEntityWithUser` respects via `ApplyUserQueryFilters`. If a deployment
disables user scoping (a documented, supported override), every other entity unscopes while this one
stays filtered. The one flag the docs treat as the source of truth for scoping silently doesn't
govern this entity. Fails *safe* today, but inconsistently.
**Fix:** thread `scopingOptions?.Enabled` into the manual filter.
**Doc impact:** `domain-map.md` → Invariants → Ownership (note the exception).

### SEC-8 🟡 `TodoListItem` lookup relies on ambient auth rather than an explicit user predicate
`application/eventHandler/PlannerTaskIsDoneChangedEventHandler.cs:50-51`

Filters on `i.Id == eventModel.TodoListItemId` only, while the sibling `SyncRoutineTodoList` filters
on both `ActivityId` and `UserId`. Scoping therefore rests entirely on the global filter, which
CLAUDE.md documents as degenerating to a no-op (`!IsAuthenticated || …`) with no ambient user. Not
reachable today (the only publisher is an authenticated request), but a future background publisher
would cross user boundaries silently.
**Fix:** add `&& i.UserId == eventModel.UserId`. **Doc impact:** none.

### SEC-9 🟡 OAuth authorization URL carries no `state` parameter
`infrastructure/extService/googleCalendar/GoogleCalendarService.cs:26-39`

Weakens CSRF protection on the authorization-code flow. Impact is limited because the code is
exchanged server-side against the authenticated user's own session, but `state` is the standard
defense and its absence is a finding on its own terms.
**Fix:** generate and validate a per-session `state`. **Doc impact:** none.

### SEC-10 🟡 Root-admin email may reach logs via `IdentityResult` descriptions
`infrastructure/persistence/seeder/default/DefaultUsersSeeder.cs:49,57,61,66,70`

The code logs `IdentityResult` error descriptions, and Identity's duplicate-username/duplicate-email
messages embed the offending value (`"UserName 'x@y.com' is already taken"`). Confidence: Low.
**Fix:** log a fixed identifier or `PiiRedactor.MaskEmail`. **Doc impact:** none.

### SEC-11 🟡 `[AuditIgnore]` missing on sensitive properties
`User.cs:20-21` · `WebExtensionActivityEntry.cs:10` · `DesktopActivityEntry.cs:12` · `Activity.cs:16`

Auditing is not wired today, so nothing leaks now — but the attributes should be correct *before*
the interceptor is enabled, or turning it on silently starts writing OAuth refresh tokens, full
URLs, window titles and user free-text into `audit_log` snapshots. Cheap now, invisible later.
**Fix:** add `[AuditIgnore]` to all four. **Doc impact:** none.

### SEC-12 🟡 Unscoped id lookups in `TodoListExtensions`
`infrastructure/persistence/extensions/TodoListExtensions.cs:33-47`

`GetDisplayOrderById` / `GetGroupIdById` take only an `id`, while the sibling `GetNextDisplayOrder`
on the same class explicitly filters `e.UserId == userId`. Safe today via the global filter, but that
safety is invisible at this call site and would evaporate under `IgnoreQueryFilters()`.
**Fix:** accept a `userId`, or document the reliance. **Doc impact:** none.

### SEC-13 🟡 `ReminderRegistrationService.CancelAsync` is not the enforcement point
`application/service/reminder/ReminderRegistrationService.cs:120-126`

Takes a bare `reminderId` with no ownership check. Both current callers source ids from user-scoped
queries, so it is safe — but a future call site forwarding a client-supplied id would let one user
cancel another's reminder registration.
**Fix:** verify the owning `userId` inside the service as defense in depth. **Doc impact:** none.

### SEC-14 🟡 Identity hardening gaps
`config/IdentityServiceExtensions.cs:32-39,109-129,111`

Three small ones: `ClockSkew` is left at the library default of **5 minutes** here while
`JwtService.cs:137` explicitly uses 30s — silently extending access-token validity; no explicit
`options.Lockout.*` policy (defaults are reasonable but unpinned, and only bite if the login flow
passes `lockoutOnFailure: true`); `RequiredLength = 8` is the historical floor.

Otherwise this file is solid — issuer/audience throw rather than falling back, the signing algorithm
is pinned via `ValidAlgorithms` (blocking algorithm-confusion), and the extension-client deny-by-default
policy is correctly wired.
**Fix:** set `ClockSkew` explicitly; pin or comment the lockout policy; consider length 10–12.
**Doc impact:** none.

---

## 6. Code quality

### CQ-1 🟠 Failed root-admin creation falls through to role assignment on a nonexistent user
`infrastructure/persistence/seeder/default/DefaultUsersSeeder.cs:56-66`

When `userManager.CreateAsync` fails, the code logs and **does not return** — it proceeds to
`AddToRoleAsync(adminUser, "Root")` and `CreateDefaultsAsync(adminUser.Id)` with `adminUser.Id` still
at its default `0`. Result is either a `Root` role row pointing at user id 0 or an exception
swallowed by the outer catch, instead of a clean diagnosable failure. The `existingAdmin` branch
already uses the correct early-return pattern.
**Fix:** `return;` after logging. **Doc impact:** none.

### CQ-2 🟠 Routine reset silently discards grace-expiry streak breaks
`infrastructure/jobs/RoutineTodoListResetJob.cs:43-47`

`CheckGrace` mutates `Streak`/`StreakGraceUntil` in memory for every period whose grace window
lapsed — but the method returns before `SaveChangesAsync` (line 50) whenever `reset.Count == 0`
(no period due for a full reset this run). Any grace expiry falling on a day with no reset is
**computed and thrown away**; the DB keeps the stale streak, and the next run recomputes from stale
data. Streak history is silently wrong.
**Fix:** track whether `CheckGrace` returned true for any period and save if either that or
`reset.Count > 0` holds.
**Doc impact:** `domain-map.md` → Business rules → Routines (`CheckGrace` bullet) — see `DOC-4`.

### CQ-3 🟠 Routine reset never unticks checklist steps
`infrastructure/jobs/RoutineTodoListResetJob.cs:21-23`

The query does `.Include(tp => tp.RoutineTodoListColl)` with **no** `.ThenInclude(t => t.Steps)`, yet
`RoutineResetService.TryReset` iterates `item.Steps` to reset each `step.IsDone`. Lazy-loading
proxies are not configured anywhere in the project, so `Steps` is always an empty collection here —
the loop is a no-op. Parent items are unticked; their steps stay ticked forever.

This directly contradicts `domain-map.md`, which states "all items **and their steps** are unticked".
**Fix:** add `.ThenInclude(t => t.Steps)`.
**Doc impact:** `domain-map.md` → Business rules → Routines — see `DOC-3`.

### CQ-4 🟠 Two `TryReset` overloads disagree on whether a reset scores the streak
`domain/service/RoutineResetService.cs:135-150`

The single-item overload advances `period.LastResetAt` to `nextReset` **without** evaluating the
streak — no `StreakOutcome`, no `RoutinePeriodCompletion` row. `ToggleStepIsDoneRoutineTodoListEndpoint.cs:27`
calls exactly this overload, while `RoutineTodoListResetJob`, `GetAllGroupedRoutineTodoListEndpoint`
and `RoutineToggleIsDoneTodoListEndpoint` all use the list-based overload that does evaluate.

If a step toggle crosses the period boundary first, it **consumes the reset cycle**: `LastResetAt`
moves past `nextReset`, no streak transition is applied, no completion row is written. That cycle's
outcome is permanently lost and the next list-based reset sees only the following cycle. Confidence: Med.
**Fix:** route step-toggle resets through the list-based overload, or make the single-item overload
refuse to advance `LastResetAt`.
**Doc impact:** `domain-map.md` → Business rules → Routines (state which paths may advance the period).

### CQ-5 🟠 One failing notification loses every idempotency marker in the sweep
`infrastructure/jobs/RoutinePeriodNudgeJob.cs:49-75`

All `EndingSoonNotifiedFor` / `GraceNotifiedFor` mutations are persisted by a **single**
`SaveChangesAsync` after the whole loop, with no try/catch around the notify calls. A transient push
or email failure on one period propagates out of `Execute`, aborting the loop — so every user already
successfully notified in that run loses their marker and **gets notified again tomorrow**, and every
period later in the enumeration is skipped entirely.
**Fix:** try/catch per period (log the period id, not PII) and continue, or save incrementally.
**Doc impact:** none — `domain-map.md` already describes the intended idempotent marking.

### CQ-6 🟠 Planner-task fan-out desyncs steps from the counts it just set
`application/eventHandler/PlannerTaskIsDoneChangedEventHandler.cs:30-42,56-67`

`SyncRoutineTodoList` / `SyncTodoListItem` force `IsDone` and snap `DoneCount` to fully-done or zero,
but load the entity **without** `.Include(x => x.Steps)` and never touch `Steps` — unlike
`BaseToggleIsDoneTodoListEndpoint.IsDoneLogic`/`ResetSteps`, which always keeps steps aligned.

The row then reads `DoneCount == TotalCount` while its steps are partially unticked. The next
`BaseToggleStepIsDoneEndpoint` call computes `allDone`/`wasFullyComplete` from those stale steps, so
the increment/decrement math desyncs from `TotalCount` and `IsDone` flips inconsistently with the
visible checklist.
**Fix:** `.Include(i => i.Steps)` and set every step to match, mirroring `ResetSteps`.
**Doc impact:** `domain-map.md` → Business rules → Completion fan-out — see `DOC-5`.

### CQ-7 🟠 To-do fan-out overwrites deliberate user state and skips reminder sync
`application/eventHandler/TodoListItemIsDoneChangedEventHandler.cs:27-28`

Three divergences from `PatchPlannerTaskStatusEndpoint`, which performs the equivalent status change:

1. **No reminder sync.** The endpoint calls `SyncForPlannerTasksAsync` on every status change; this
   handler doesn't. A task completed via its parent to-do item keeps its reminder scheduled, so the
   user is nudged about finished work. Silent — no exception, no log.
2. **`ActualStartTime`/`ActualEndTime` not cleared** when reverting to `NotStarted`, so a task shown
   as not-started retains stale timestamps, corrupting any duration reporting that reads them.
3. **`Cancelled` is overwritten.** The loop unconditionally forces every matching task to `Completed`
   or `NotStarted`, silently un-cancelling a task the user deliberately cancelled that day.

Root cause is duplicated status logic across two call sites that have already drifted.
**Fix:** extract a shared `ApplyPlannerTaskStatus(task, status)` used by both; skip `Cancelled`.
**Doc impact:** `domain-map.md` → Business rules → Completion fan-out (document the `Cancelled` carve-out).

### CQ-8 🟠 Two events are never published — their handlers are dead code
`application/eventHandler/ActivityAddedToHistoryEventHandler.cs` · `ActivityCreatedIsOnToDoListEventHandler.cs`

Repo-wide searches (portal **and** the `framework/` submodule) for `ActivityAddedToHistoryEvent` and
`ActivityCreatedIsOnTodoListEvent` find only the event records and their handlers — **no
`PublishAsync` call site anywhere**. Both handlers are registered via `IEventHandler<T>` and wired
through DI, so they look live.

The user-visible consequence: creating an activity flagged "is on to-do list" does **not** create the
`TodoListItem`. `domain-map.md` lists both as wired events, distinguishing only two *other* events as
"declared-but-unhandled" — so the docs actively assert this works.
**Fix:** wire the publish calls, or delete both event/handler pairs.
**Doc impact:** `domain-map.md` → Events line — see `DOC-2`.

### CQ-9 🟠 View-refresh failure surfaces as a 500 *after* the data has committed
`infrastructure/persistence/interceptors/SuggestionPatternRefreshInterceptor.cs:36-45`

The refresh runs in `SavedChangesAsync` — i.e. after commit — with no try/catch. Any failure (42P01
when a view is missing, a `REFRESH CONCURRENTLY` rejection when the view lacks a unique index, a
lock-wait timeout) propagates as an unhandled exception. The client gets a 500 for an operation that
**succeeded and is durably persisted**, and any retry hits a duplicate-save path.
**Fix:** wrap each refresh in its own try/catch, log the view name, let the save result stand.
**Doc impact:** none.

### CQ-10 🟠 Stale refresh flags survive a failed save
`infrastructure/persistence/interceptors/SuggestionPatternRefreshInterceptor.cs:23-25,47-49`

`_refreshPlanner`/`_refreshHistory`/`_refreshTemplate` are set in `SavingChangesAsync` and cleared
only at the end of `SavedChangesAsync`. There is no `SaveChangesFailedAsync` override, so if a save
throws in between, the flags stay `true` and the **next** save on that scoped context — even one
touching none of the three types — triggers a spurious full view refresh.
**Fix:** override `SaveChangesFailedAsync` to reset the flags. **Doc impact:** none.

### CQ-11 🟠 No compensation when a post-commit reminder cancel fails
`application/service/reminder/ReminderRegistrationService.cs:120-126`

`DeleteReminderEndpoint.AfterSave` / `DeletePlannerTaskEndpoint.AfterSave` cancel *after* the portal
delete has committed — deliberately, to avoid publishing against a nonexistent row. But there is no
try/catch, retry or outbox: if `registry.CancelAsync` throws, the portal row is gone while the
module's `ReminderDefinition` survives and keeps firing, now referencing a deleted id.

This produces precisely the orphaned-reminder class of bug the class's own doc comments say the
design exists to prevent. Confidence: Med.
**Fix:** catch and log distinctly, and/or queue a reconciliation retry.
**Doc impact:** none.

---

## 9. Performance hotspots

| Where | Issue | Impact | Mitigation | ID |
|---|---|---|---|---|
| `SuggestionPatternRefreshInterceptor.cs:35-45` | `REFRESH MATERIALIZED VIEW CONCURRENTLY` runs **synchronously on the request thread** for every save touching `PlannerTask`/`ActivityHistory`/`Calendar` | O(view size), not O(change size) — a one-row edit pays for a full pattern rebuild. Direct p99 inflation on the three hottest entities | Mark views dirty; refresh on an interval via `Sydowwe.Scheduler` | `PERF-1` 🟠 |
| `SuggestionPatternRefreshInterceptor.cs:36-45` | `REFRESH CONCURRENTLY` on one view **serializes** against a second concurrent refresh of the same view | Under normal concurrent planner writes, requests queue behind each other's refresh → thread/connection-pool pressure, not just latency | Same fix as `PERF-1` | `PERF-2` 🟠 |
| `ReminderRegistrationService.cs:122-139` | `CancelManyAsync` / `SyncForPlannerTasksAsync` loop with sequential `await`, each iteration doing its own registry round trip **plus** its own `PlannerTasks` query and possible `SaveChangesAsync` | N+1-shaped; a batch planner-task delete multiplies into N sequential DB + module round trips, and a mid-loop failure leaves an undefined subset processed | Batch the task/timezone lookups once per call; bulk registry ops if the contract allows | `PERF-3` 🟠 |
| `GoogleCalendarService.cs:49-58` | New `GoogleAuthorizationCodeFlow` + `CalendarService` (and underlying `HttpClient`) per call, never disposed, on an `ISingletonService` | Socket exhaustion / per-call TLS handshake under load; undisposed `IDisposable`s | Cache the flow (no per-user state); dispose the `CalendarService` | `PERF-4` 🟡 |
| `GoogleSignInService.cs:17-25` | Same pattern — per-request flow, never disposed | Handle/socket leak on the sign-in path | `using`, or inject a shared client | `PERF-4` 🟡 |
| `Program.cs:128` + `AppDbContext.cs:157-160` | Duplicate unconditional EF console logging | Every SQL statement serialized and written to stdout **twice**, in Production | Delete the override; gate the other | `PERF-5` 🟡 |
| `Program.cs:414` | `reader.ReadToEndAsync().Result` — sync-over-async inside the Serilog enrichment delegate, on every non-GET request | Blocks a threadpool thread on the hot write path; contributes to starvation under load | Removed along with `SEC-1`'s fix | `PERF-6` 🟡 |
| `ReminderRegistrationService.cs:54-56` | `PlannerTasks` lookup is not `AsNoTracking` despite being read-only | Change-tracker overhead on a hot per-reminder path | `.AsNoTracking()` | `PERF-7` 🟡 |
| `RoutinePeriodNudgeJob.cs:49-69` | Notifications sent sequentially | Lengthens the daily sweep as period count grows; low priority at daily cadence | Bounded `Task.WhenAll` | `PERF-8` 🟡 |

### Indexes that should exist

Derived from query shapes in the reviewed files. **Unverified** — the entity-configuration fan-out
died before covering most of these, so some may already exist.

| Table | Index | Why | ID |
|---|---|---|---|
| `reminder` | `(user_id, remind_at)` | The day view filters `RemindAt` by the user's local-day range; without it this is a per-user range scan that degrades as reminder volume grows | `PERF-9` 🟡 |
| `activity_history` | `(user_id, start_timestamp)` | History dashboards are user + date-range scans; also backs `mv_activity_history_pattern` | `PERF-10` 🟡 (unverified) |
| `planner_task` | `(user_id, calendar_id)` / date-range | Day view is the SPA's main read | `PERF-11` 🟡 (unverified) |
| `routine_period_completion` | `(time_period_id, period_start desc)` | Streak history lookups bounded by `HistoryDepth` | `PERF-12` 🟡 (unverified) |
| `refresh_token` | token-lookup column; expiry column | Every refresh does the first; `RefreshTokenCleanupService` sweeps on the second | `PERF-13` 🟡 (unverified) |

---

## Code/doc drift

Checked against `AdhdTimeOrganizer/docs/domain-map.md`, which landed after the fan-out started.

### DOC-1 🟠 `PortalEndpointHelper` does not exist
`domain-map.md:265` lists it under "Services, jobs and infrastructure" at
`application/helper/PortalEndpointHelper.cs`, with "Role arrays + `GetVerifiedUser()` closed over the
portal `User`". CLAUDE.md describes it in equal detail.

A glob for `**/PortalEndpointHelper.cs` and a full-tree text search for `PortalEndpointHelper`,
`GetVerifiedUser` and `GetUserOrHigherRoles` return **nothing**. `application/helper/` contains only
`TaskPlannerHelper.cs`. Verified directly by me, not by an agent.

**Which side is wrong:** unknown — either the file was never created, or it was removed without
updating either doc. If endpoints currently call `IEndpoint.GetUserRole()` / `GetAdminRole()` from
the framework directly, the doc is simply stale and should be deleted.
**Doc impact:** `domain-map.md` → Navigation index (remove the row) **and** CLAUDE.md → FastEndpoints
Base Classes (the `PortalEndpointHelper` paragraph).

### DOC-2 🟠 Two events documented as wired are never published
`domain-map.md:280-283` lists `ActivityAddedToHistoryEvent` and `ActivityCreatedIsOnTodoListEvent`
among the live events, explicitly separating out only two others as "declared-but-unhandled". Per
`CQ-8`, neither of these two is ever published either — they are handled but never raised, which is
the mirror-image dead state the doc doesn't have a category for.
**Which side is wrong:** the code, if the features are wanted; otherwise both. **Doc impact:**
`domain-map.md` → Events (either remove them or mark them "handled but never published").

### DOC-3 🟠 "all items and their steps are unticked" — steps are not unticked
`domain-map.md:146` states the reset unticks items *and their steps*. Per `CQ-3` the job's query
never loads `Steps`, so the step-reset loop is a no-op.
**Which side is wrong:** the code. **Doc impact:** none once `CQ-3` is fixed — the doc describes the
intended behavior correctly.

### DOC-4 🟠 `CheckGrace` "meant to be called before any reset evaluation" — its result is discarded
`domain-map.md:147-148`. Per `CQ-2`, `CheckGrace` *is* called first but its mutations are dropped
whenever no period is due for reset.
**Which side is wrong:** the code. **Doc impact:** none once `CQ-2` is fixed.

### DOC-5 🟠 "`DoneCount` is snapped … for step-counted items" — the steps themselves are not
`domain-map.md:159`. Per `CQ-6` the counts are snapped but `Steps` is neither loaded nor updated,
leaving the two representations of the same state contradicting each other.
**Which side is wrong:** the code. **Doc impact:** none once `CQ-6` is fixed.

### DOC-6 🟡 `RoutineTimePeriod`'s two unique indexes — seeder compliance unverified
`domain-map.md:63,67` documents **two** per-user unique indexes: `(UserId, Text)` and
`(UserId, LengthInDays)`. CLAUDE.md warns that `RoutineTimePeriodSeeder`'s `Collides` must cover
**both** or a latent 23505 ships.

The agent assigned to that seeder was killed by the session limit, so this is **unverified**. Flagged
here so it is not lost — it is a concrete, cheap check.
**Which side is wrong:** unknown. **Doc impact:** none (the doc is believed correct).

---

## 🟡 Nits appendix

Grouped; each is one line from a fragment. Full detail in `findings/`.

**Correctness-adjacent**
- `CQ-12` `RoutineResetService.cs:34-50` — monthly/yearly `ComputeNextReset` always steps exactly one
  period, never catching up to `now`. A dormant routine manufactures one fabricated
  `RoutinePeriodCompletion` (and streak transition) per missed cycle, from current item state.
- `CQ-13` `RoutineResetService.cs:15,69` — weekly path resets at 00:00 UTC, monthly/yearly at 02:00
  UTC. Undocumented asymmetry that will bite the next nudge-window feature.
- `CQ-14` `BasePlannerTask.cs:7-9` — `IsNextDay` commented out; `TimeOnly` start/end cannot express an
  overnight task, so duration goes negative. Inherited by all three planner-task types.
- `CQ-15` `BasePlannerTask.cs:19` — `IsOptional` is `Importance?.Importance == 666`, an unnamed
  sentinel. (`domain-map.md` documents it, so the doc is right; the code wants a constant.)
- `CQ-16` `TaskPlannerHelper.cs:18` — `TasksOverlap` assumes same-day non-wrapping intervals; pairs
  with `CQ-14`.
- `CQ-17` `Activity.cs:39-44` — `Clone()` uses `MemberwiseClone`, sharing navigation-collection
  *references* with the source. Safe only because the sole caller fetches via `FindAsync` with no
  `Include`.
- `CQ-18` `BaseTodoListItem.cs:9-14` — `DoneCount`/`TotalCount` invariant lives only in a DB check
  constraint and is re-derived in two handlers; `Steps` is a publicly settable collection.
- `CQ-19` `TodoListEntityConfigurationExtensions.cs:16` — the `done_count <= total_count` check is
  bypassed when either column is NULL (Postgres treats NULL checks as satisfied).
- `CQ-20` `GoogleSignInService.cs:66-69` — catch-all maps tampered/expired tokens to
  `InternalServerError`; client errors reported as 5xx pollute alerting.
- `CQ-21` `Program.cs:259-274` — `pageUrl` is added to CORS `origins` unconditionally and is `null`
  when `PAGE_URL` is unset; `WithOrigins` on a null entry throws or silently misconfigures.
- `CQ-22` `SeedUserIdProvider.cs:40` — `Helper.GetEnvVar("ROOT_ADMIN_EMAIL")` throws when unset, but
  the caller only handles the null case; a missing `.env` entry crashes dev seeding instead of
  skipping gracefully.
- `CQ-23` `EntityWithActivityBuilderExtensions.cs:11-26` — `isRequired` is caller-settable against a
  non-nullable `long ActivityId`; `isRequired: false` either fights the CLR type or is silently ignored.
- `CQ-24` `Reminder.cs:50` — `LeadOffsetsMinutes` "≤ 0 and unique" is enforced only by the validator
  and the module registry, never by the column. Any other write path can violate it.
- `CQ-25` `Reminder.cs:47` — doc says UTC but the type is plain `DateTime` with no `Kind` guarantee.
- `CQ-26` `DesktopActivityEntry.cs:8-9` — nothing enforces `RecordDate == DateOnly.FromDateTime(WindowStart)`,
  so a row can be misfiled into the wrong partition.
- `CQ-27` `WebExtensionActivityEntry.cs:8` — "Always 1-min aligned" is an unenforced comment.

**Consistency / hygiene**
- `CQ-28` `AppCommandDbContextFactory.cs:16` — no `MigrationsAssembly` override, unlike `Program.cs`.
  Resolves identically today; asymmetric by inspection.
- `CQ-29` `DefaultUsersSeeder.cs:59` — hard-coded `"Root"` instead of `nameof(UserRoleEnum.Root)`.
  A typo would silently produce a rootless admin with no compile-time signal.
- `CQ-30` `DefaultUsersSeeder.cs:34-46` — the `overrideData` branch sits outside the try/catch that
  covers the create path, and discards `IdentityResult.Succeeded` from `UpdateAsync`/`ResetPasswordAsync`.
- `CQ-31` `UserDefaultsService.cs:20` and `RoutinePeriodNotificationService.cs:76` — `catch (Exception)`
  swallows `OperationCanceledException`, reporting shutdown/disconnect as a failure.
- `CQ-32` `ActivityAddedToHistoryEventHandler.cs:26` — `logger.LogError(result.ErrorMessage)` passes a
  runtime string as the message template; braces in it would be parsed as Serilog holes.
- `CQ-33` `Program.cs:167-176,199-203,372-373` — three pieces of TEMP debug scaffolding left in the
  composition root: a `StartNow()` trigger that fires `RoutineTodoListResetJob` on **every boot**, a
  `ValidationSchemaProcessor` removal degrading dev Swagger, and `Console.WriteLine` stack-trace dumps
  on `ApplicationStopping`.
- `CQ-34` `SuggestionPatternViewInstaller.cs:30-31` — resource matching uses `Contains` rather than an
  anchored prefix, and creation order is alphabetical-by-resource-name with no explicit dependency
  declaration.
- `CQ-35` `TodoListExtensions.cs:22,27` — parameter named `timePeriodId` is actually filtered against
  `TaskPriorityId` on the `TodoListItem` overload.
- `CQ-36` `SerilogConfig.cs:54,57-71` — production detection reads the raw `ASPNETCORE_ENVIRONMENT`
  env var instead of `context.HostingEnvironment.IsProduction()`; the table is named `warning_logs`
  but its minimum level is `Information`; `WriteTo.PostgreSQL` is called with 11 positional args.
- `CQ-37` File/class naming mismatches: `ActivityCreatedIsOnToDoListEventHandler.cs` (capital D) vs
  `ActivityCreatedIsOnTodoListEventHandler` (lowercase); `NotificationsUserFkConfiguration.cs` holds
  three differently-named top-level classes.
- `CQ-38` `User.cs:28-32` — `PhoneNumber`/`PhoneNumberConfirmed` re-override `[NotMapped]` already
  declared identically on `BaseUser`.

**Clean files** — no issues found: `PortalRoleCatalog.cs`, `EntityWithUserBuilderExtensions.cs`
(verified against the framework target: `IsRequired()` + `Cascade` both preserved, so no GDPR erasure
gap), `BaseEntityWithUser.cs` and `BaseLookupWithUser.cs` (both correctly plain closing types),
`PortalAuthorizationPolicies.cs`, `DependencyInjectionExtensions.cs` (the required
`Except(ModuleAssemblies)` guard is present), `ModuleServiceExtensions.cs`.
