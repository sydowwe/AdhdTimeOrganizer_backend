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

### SEC-1 🔴 ✅ FIXED Plaintext passwords and PII persisted to the log database
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
**Resolution:** removed the request-body buffering/capture block from `EnrichDiagnosticContext`
entirely (`Program.cs`) — `RequestBody` is no longer set on the diagnostic context, so it can't reach
the sink. `RequestHost`/`RequestScheme`/`UserAgent`/`ClientIP` are left as-is (not the flagged leak).
The "commented-out columns look deliberately excluded" trap noted above is now called out explicitly
with a comment in `SerilogConfig.cs` (see `CQ-36`'s resolution), and the sink now carries a 90-day
`retentionTime` (native to the `Serilog.Sinks.Postgresql.Alternative` package) instead of growing
unbounded.

### SEC-2 🟠 ✅ FIXED Google Calendar OAuth refresh token stored in plaintext
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
**Resolution:** two passes.

*First pass:* added `[AuditIgnore]` to both `GoogleOAuthUserId` and `GoogleCalendarRefreshToken` on
`User.cs`, so an eventual audit-interceptor enable won't snapshot either into `audit_log`.

*Second pass — the encryption itself:*
- `UserEntityConfiguration` now calls `builder.EncryptedColumnNullable(u => u.GoogleCalendarRefreshToken)`
  instead of `.HasMaxLength(500)`. `GoogleOAuthUserId` is deliberately left plaintext — it is the
  lower-sensitivity half and encrypting it buys little.
- **New framework method** (submodule commit): `EncryptedColumn` takes
  `Expression<Func<TEntity, string>>`, which a `string?` property cannot bind to. It could not be a
  plain overload — `string` and `string?` are the same type to overload resolution (CS0111), unlike
  the `EnumColumn` pair where `TEnum?` is genuinely different. So it is a distinct
  `EncryptedColumnNullable`, backed by a new `NullableEncryptedStringConverter`
  (`ValueConverter<string?, string>`) added next to `EncryptedStringConverter`. The nullable
  converter exists purely to satisfy EF's `HasConversion` signature and silence CS8620 — EF never
  invokes a value converter for `null`, so a null token stays a SQL NULL rather than becoming an
  encrypted empty string.
- `FIELD_ENCRYPTION_KEY` generated (32 random bytes, base64) and added to
  `AdhdTimeOrganizer/.env`. ⚠ **This is now a hard boot requirement, not an optional feature:**
  `EncryptedColumn*` evaluates `AesGcmFieldEncryptor.Shared` *inside* `OnModelCreating`, and that
  ctor calls `Helper.GetEnvVar`, which throws `EnvironmentVariableMissingException` when unset. Any
  environment without the key — `.env.prod`, CI, the integration-test container — now fails to build
  the EF model at startup. **`.env.prod` does not have it yet.**
- **Migration:** `varchar(500)` → `text`, schema-only. Scaffolded as
  `20260810094941_EncryptGoogleCalendarRefreshToken` but left to the repo owner to finish/adjust.
- **Existing rows stay plaintext.** `AesGcmFieldEncryptor.Decrypt` passes any value lacking the
  `enc:v1:` prefix through untouched, so already-stored tokens keep working and are encrypted only on
  their next write. Full coverage of currently-connected users needs a one-off re-encrypt pass, or
  accepting that they are protected from their next token refresh onward.
- ⚠ Rotating `FIELD_ENCRYPTION_KEY` makes every existing `enc:v1:` token undecryptable; the `v1`
  prefix exists to allow a staged `v2` rotation, but no rotation tooling is written.

*Doc impact closed:* `domain-map.md` → Invariants now states `GoogleCalendarRefreshToken` is
AES-256-GCM encrypted via `EncryptedColumnNullable`, `GoogleOAuthUserId` stays plaintext, both carry
`[AuditIgnore]`, and that existing rows stay plaintext until their next write.

### SEC-3 🟠 ✅ FIXED Every SQL statement logged to console in Production — twice
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
**Resolution:** `Program.cs:128`'s `.LogTo(Console.WriteLine)` is now gated behind
`isDevelopment`. `AppDbContext.cs`'s `OnConfiguring` override (the duplicate, unconditional
`LogTo`) has been deleted entirely, along with the leftover tutorial-style comment above it.

### SEC-4 🟠 ✅ PARTIALLY FIXED Desktop window titles stored in plaintext with no retention
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
**Resolution:** added `[AuditIgnore]` to `WindowTitle`/`ExecutablePath`, and a daily retention purge
(`PurgeExpiredActivityTrackingEntriesJob`, `ActivityTrackingRetentionOptions`, section
`ActivityTrackingRetention`, 3y age purge / no keep-last-N floor) that hard-deletes both
`DesktopActivityEntry` and `WebExtensionActivityEntry` rows past the horizon — wired into the
existing portal Quartz schedule in `Program.cs`.

**Encryption pass — `ExecutablePath` encrypted, `WindowTitle` deliberately not.**
`builder.EncryptedColumn(x => x.ExecutablePath)` is now on `DesktopActivityEntryConfiguration`.
`ExecutablePath` is write-only — set at ingest, never filtered, grouped, sorted, indexed or projected
anywhere — so randomized encryption costs nothing. Needs a migration (`varchar(2048)` → `text`).

⚠ **`WindowTitle` cannot be encrypted, and the fix originally proposed above would not have worked.**
This finding assumed the only obstacle was the unique index, solvable with a companion hash column.
That is wrong. `FetchTableDistinctDesktopEntry` is a fully server-side `IQueryable` that:
- `GroupBy`s on `WindowTitle` (line 47) — under randomized encryption every row becomes its own
  group, so the distinct-entries grid silently returns one row per raw row. No error, just wrong;
- filters it via `ApplyStringMatchFilter` (line 85), which builds `string.Contains` / SQL `LIKE` / `=`
  into the query — four match modes (Exact · Contains · Wildcard · Regex), all SPA-exposed;
- sorts and paginates on the projected value.

A hash column recovers grouping and `Exact` only. `Contains`, `Wildcard` and `Regex` over ciphertext
are unrecoverable by any deterministic-hash scheme — they need the plaintext in the database.
Encrypting `WindowTitle` therefore means **removing three of four filter modes from a live grid**,
which is a product decision, not a refactor. The two dashboard endpoints that also touch
`WindowTitle` (`DesktopPieChartEndpoint`, `DesktopProcessDetailsEndpoint`) both `ToListAsync` first
and group in memory, so they would be unaffected — the grid is the sole blocker.

The mitigations already shipped (3y retention purge, `[AuditIgnore]`) stand. A rationale comment is
now in `DesktopActivityEntryConfiguration` so the next person doesn't re-derive this.
*Doc impact closed:* `domain-map.md` → Business rules → Time tracking now states the 3-year purge
window and job name (shared with `SEC-5`).

### SEC-5 🟠 ✅ FIXED Browsing history retained indefinitely
`domain/model/entity/activityTracking/WebExtensionActivityEntry.cs:9-10`

`Domain` and `Url` (up to 2048 chars) are the user's browsing history. No purge job exists. The
entity's query filter (`RecordDate >= CurrentPartitionDate`) **hides** old rows from EF reads but
deletes nothing — raw SQL, reporting and the partitions themselves still hold years of URL history.
A filter that looks like retention but isn't is worse than no filter, because it suppresses the
symptom that would prompt someone to add a real purge.

**Fix:** `RetentionOptions` subclass + purge handler modeled on `PurgeExpiredRunLogsJobHandler`;
drop whole partitions where possible. `[AuditIgnore]` on `Url`.
**Doc impact:** `domain-map.md` → Business rules → Time tracking (retention window).
**Resolution:** covered by the same `PurgeExpiredActivityTrackingEntriesJob` added for `SEC-4` — it
`IgnoreQueryFilters()`s and hard-deletes `WebExtensionActivityEntry` rows past the 3y horizon
alongside `DesktopActivityEntry`. Added `[AuditIgnore]` to `Domain` and `Url`. Did not drop whole
partitions (row-level `ExecuteDeleteAsync` matches the Scheduler/Reminders precedent and keeps the
partition boundaries simple; can move to partition-drop later if purge volume becomes a problem).
*Doc impact closed:* see `SEC-4`'s resolution — same domain-map.md paragraph covers both entities.

### SEC-6 🟡 ✅ FIXED `Include Error Detail=true` puts parameter values in exception messages
`config/DatabaseStringsHelper.cs:7,10`

Both connection strings inherit this from `Helper.GetDatabaseConnectionString`. Npgsql then embeds
actual parameter values in exception messages, which — given `SEC-1`/`SEC-3` and no redaction — flow
straight into logs. **Fix:** gate behind `IsDevelopment()`. **Doc impact:** none.
**Resolution:** `Helper.GetDatabaseConnectionString` lives in the framework submodule, so rather than
a two-repo edit, `DatabaseStringsHelper` now appends `;Include Error Detail=false` in Production
(Npgsql connection strings take the last occurrence of a duplicate key, so this overrides the
framework default without touching the submodule).

### SEC-7 🟡 ✅ FIXED `WebExtensionActivityEntry` filter ignores the `UserScoping:Enabled` switch
`infrastructure/persistence/AppDbContext.cs:141-149`

The hand-written filter is gated only on `loggedUserService != null`, not on `UserScopingOptions.Enabled`
— the switch every other `IEntityWithUser` respects via `ApplyUserQueryFilters`. If a deployment
disables user scoping (a documented, supported override), every other entity unscopes while this one
stays filtered. The one flag the docs treat as the source of truth for scoping silently doesn't
govern this entity. Fails *safe* today, but inconsistently.
**Fix:** thread `scopingOptions?.Enabled` into the manual filter.
**Doc impact:** `domain-map.md` → Invariants → Ownership (note the exception).
**Resolution:** `OnModelCreating` now pulls `IOptions<UserScopingOptions>` from the application
service provider (same lookup `ApplyUserScopingIfEnabled` does) and only applies the `UserId` half of
the combined filter when `Enabled` is true; falls back to the partition-date-only filter otherwise.
No behavior change today since `Program.cs` defaults `UserScoping:Enabled = true`.

### SEC-8 🟡 ✅ FIXED `TodoListItem` lookup relies on ambient auth rather than an explicit user predicate
`application/eventHandler/PlannerTaskIsDoneChangedEventHandler.cs:50-51`

Filters on `i.Id == eventModel.TodoListItemId` only, while the sibling `SyncRoutineTodoList` filters
on both `ActivityId` and `UserId`. Scoping therefore rests entirely on the global filter, which
CLAUDE.md documents as degenerating to a no-op (`!IsAuthenticated || …`) with no ambient user. Not
reachable today (the only publisher is an authenticated request), but a future background publisher
would cross user boundaries silently.
**Fix:** add `&& i.UserId == eventModel.UserId`. **Doc impact:** none.
**Resolution:** added the explicit `UserId` predicate to the `TodoListItem` lookup.

### SEC-9 🟡 ✅ FIXED OAuth authorization URL carries no `state` parameter
`infrastructure/extService/googleCalendar/GoogleCalendarService.cs:26-39`

Weakens CSRF protection on the authorization-code flow. Impact is limited because the code is
exchanged server-side against the authenticated user's own session, but `state` is the standard
defense and its absence is a finding on its own terms.
**Fix:** generate and validate a per-session `state`. **Doc impact:** none.
**Resolution:** `GoogleCalendarService.GenerateState()` produces a random 32-byte hex token;
`GetGoogleCalendarAuthUrlEndpoint` sets it as an HttpOnly/Secure/SameSite=Strict cookie
(`google-oauth-state`, scoped to `/user/google-calendar`, 10 min expiry) and appends it to the
authorization URL as `&state=`. `ConnectGoogleCalendarEndpoint` now requires `State` on the request,
compares it against the cookie, deletes the cookie either way, and 400s on mismatch/absence before
exchanging the code. Also (same file) wrapped `ExchangeCodeForTokenAsync` in a
`try/catch (TokenResponseException)` returning `null` (already the caller's existing 400 path) so a
revoked/invalid code no longer throws a raw exception, and cached the `GoogleAuthorizationCodeFlow`
as a `Lazy<T>` field instead of rebuilding it (and its `HttpClient`) per call — partial fix for
`PERF-4` below.

### SEC-10 🟡 ✅ FIXED Root-admin email may reach logs via `IdentityResult` descriptions
`infrastructure/persistence/seeder/default/DefaultUsersSeeder.cs:49,57,61,66,70`

The code logs `IdentityResult` error descriptions, and Identity's duplicate-username/duplicate-email
messages embed the offending value (`"UserName 'x@y.com' is already taken"`). Confidence: Low.
**Fix:** log a fixed identifier or `PiiRedactor.MaskEmail`. **Doc impact:** none.
**Resolution:** all `logger.LogError` calls in this seeder now log fixed, generic messages instead of
`IdentityResult.ToString()` / error descriptions, so no offending value can leak into logs.

### SEC-11 🟡 ✅ FIXED `[AuditIgnore]` missing on sensitive properties
`User.cs:20-21` · `WebExtensionActivityEntry.cs:10` · `DesktopActivityEntry.cs:12` · `Activity.cs:16`

Auditing is not wired today, so nothing leaks now — but the attributes should be correct *before*
the interceptor is enabled, or turning it on silently starts writing OAuth refresh tokens, full
URLs, window titles and user free-text into `audit_log` snapshots. Cheap now, invisible later.
**Fix:** add `[AuditIgnore]` to all four. **Doc impact:** none.
**Resolution:** `DesktopActivityEntry.WindowTitle`/`ExecutablePath`, `Activity.Text`,
`WebExtensionActivityEntry.Domain`/`Url`, and `User.GoogleOAuthUserId`/`GoogleCalendarRefreshToken`
all now carry `[AuditIgnore]`.

### SEC-12 🟡 ✅ FIXED Unscoped id lookups in `TodoListExtensions`
`infrastructure/persistence/extensions/TodoListExtensions.cs:33-47`

`GetDisplayOrderById` / `GetGroupIdById` take only an `id`, while the sibling `GetNextDisplayOrder`
on the same class explicitly filters `e.UserId == userId`. Safe today via the global filter, but that
safety is invisible at this call site and would evaporate under `IgnoreQueryFilters()`.
**Fix:** accept a `userId`, or document the reliance. **Doc impact:** none.
**Resolution:** both methods now accept a `userId` parameter and filter `e.UserId == userId`
alongside the id predicate, matching `GetNextDisplayOrder`'s pattern; the sole caller
(`BaseChangeDisplayOrderTodoListEndpoint`) threads `User.GetId()` through `CalculateNewOrderAsync`
and `RebalanceDisplayOrdersAsync` to the new parameter.

### SEC-13 🟡 ✅ FIXED `ReminderRegistrationService.CancelAsync` is not the enforcement point
`application/service/reminder/ReminderRegistrationService.cs:120-126`

Takes a bare `reminderId` with no ownership check. Both current callers source ids from user-scoped
queries, so it is safe — but a future call site forwarding a client-supplied id would let one user
cancel another's reminder registration.
**Fix:** verify the owning `userId` inside the service as defense in depth. **Doc impact:** none.
**Resolution:** documented the precondition prominently in `CancelAsync`'s XML doc (both call sites
source the id from a user-scoped query or a prior `AuthorizeAsync` check) rather than adding a
second ownership check the service has no `userId` parameter to verify against today.

### SEC-14 🟡 ✅ FIXED Identity hardening gaps
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
**Resolution:** set `ClockSkew = TimeSpan.FromSeconds(30)` to match `JwtService`; explicitly pinned
`Lockout.MaxFailedAccessAttempts`/`DefaultLockoutTimeSpan`/`AllowedForNewUsers` (verified
`PasswordSignInFlow.RunAsync` calls `CheckPasswordSignInAsync(user, password, true)`, so lockout is
actually live); raised `RequiredLength` to 10.

---

## 6. Code quality

### CQ-1 🟠 ✅ FIXED Failed root-admin creation falls through to role assignment on a nonexistent user
`infrastructure/persistence/seeder/default/DefaultUsersSeeder.cs:56-66`

When `userManager.CreateAsync` fails, the code logs and **does not return** — it proceeds to
`AddToRoleAsync(adminUser, "Root")` and `CreateDefaultsAsync(adminUser.Id)` with `adminUser.Id` still
at its default `0`. Result is either a `Root` role row pointing at user id 0 or an exception
swallowed by the outer catch, instead of a clean diagnosable failure. The `existingAdmin` branch
already uses the correct early-return pattern.
**Fix:** `return;` after logging. **Doc impact:** none.
**Resolution:** added `return;` immediately after logging a failed `CreateAsync`.

### CQ-2 🟠 ✅ FIXED Routine reset silently discards grace-expiry streak breaks
`infrastructure/jobs/RoutineTodoListResetJob.cs:43-47`

`CheckGrace` mutates `Streak`/`StreakGraceUntil` in memory for every period whose grace window
lapsed — but the method returns before `SaveChangesAsync` (line 50) whenever `reset.Count == 0`
(no period due for a full reset this run). Any grace expiry falling on a day with no reset is
**computed and thrown away**; the DB keeps the stale streak, and the next run recomputes from stale
data. Streak history is silently wrong.
**Fix:** track whether `CheckGrace` returned true for any period and save if either that or
`reset.Count > 0` holds.
**Doc impact:** `domain-map.md` → Business rules → Routines (`CheckGrace` bullet) — see `DOC-4`.
**Resolution:** added a `graceChanged` accumulator (`graceChanged |= RoutineResetService.CheckGrace(period, now)`)
and gated the early return on `reset.Count == 0 && !graceChanged`, so a grace break with no period
reset in the same run still reaches `SaveChangesAsync`.

### CQ-3 🟠 ✅ FIXED Routine reset never unticks checklist steps
`infrastructure/jobs/RoutineTodoListResetJob.cs:21-23`

The query does `.Include(tp => tp.RoutineTodoListColl)` with **no** `.ThenInclude(t => t.Steps)`, yet
`RoutineResetService.TryReset` iterates `item.Steps` to reset each `step.IsDone`. Lazy-loading
proxies are not configured anywhere in the project, so `Steps` is always an empty collection here —
the loop is a no-op. Parent items are unticked; their steps stay ticked forever.

This directly contradicts `domain-map.md`, which states "all items **and their steps** are unticked".
**Fix:** add `.ThenInclude(t => t.Steps)`.
**Doc impact:** `domain-map.md` → Business rules → Routines — see `DOC-3`.
**Resolution:** added `.ThenInclude(t => t.Steps)` to the query. `TryReset` calls `item.SetDone(false)`
(the `CQ-18` helper), which now has the loaded `Steps` collection to unset.

### CQ-4 🟠 ✅ FIXED Two `TryReset` overloads disagree on whether a reset scores the streak
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
**Resolution:** the single-item `TryReset(period, item, now)` overload no longer sets
`period.LastResetAt`. It still un-ticks the touched item so the UI looks fresh, but
`ComputeNextReset` keeps reporting the same due reset until the list-based overload (background job
or grouped read) runs, which remains the sole place the streak transition and
`RoutinePeriodCompletion` row are produced.
*Doc impact closed:* `domain-map.md` → Business rules → Routines now states which overload may
advance `LastResetAt`/evaluate the streak and which only un-ticks for a fresh UI.

### CQ-5 🟠 ✅ FIXED One failing notification loses every idempotency marker in the sweep
`infrastructure/jobs/RoutinePeriodNudgeJob.cs:49-75`

All `EndingSoonNotifiedFor` / `GraceNotifiedFor` mutations are persisted by a **single**
`SaveChangesAsync` after the whole loop, with no try/catch around the notify calls. A transient push
or email failure on one period propagates out of `Execute`, aborting the loop — so every user already
successfully notified in that run loses their marker and **gets notified again tomorrow**, and every
period later in the enumeration is skipped entirely.
**Fix:** try/catch per period (log the period id, not PII) and continue, or save incrementally.
**Doc impact:** none — `domain-map.md` already describes the intended idempotent marking.
**Resolution:** wrapped each period's nudge+grace-warning block in its own try/catch (re-throwing
`OperationCanceledException` on shutdown, logging period id — not PII — for anything else), so one
period's failure no longer aborts the loop or loses markers already set for earlier periods. Left the
single end-of-loop `SaveChangesAsync` as-is — the try/catch already prevents an exception from
reaching it, so incremental saving wasn't needed.

### CQ-6 🟠 ✅ FIXED Planner-task fan-out desyncs steps from the counts it just set
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
**Resolution:** both `SyncRoutineTodoList`/`SyncTodoListItem` now `.Include(x => x.Steps)` and set
every step's `IsDone` to match the item's new state. Also wrapped the handler body in a try/catch
(logging, not rethrowing) since `PatchPlannerTaskStatusEndpoint` awaits this via
`PublishAsync(Mode.WaitForAll, …)` after its own commit — an unguarded exception here was turning an
already-committed status change into a false 500.

### CQ-7 🟠 ✅ FIXED To-do fan-out overwrites deliberate user state and skips reminder sync
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
**Resolution:** added `PlannerTask.ApplyStatus(newStatus)` (sets `Status`, clears actual times for
Cancelled/NotStarted) and used it from both `PatchPlannerTaskStatusEndpoint` and this handler; the
handler now skips tasks already `Cancelled`, calls `IReminderRegistrationService.SyncForPlannerTasksAsync`
after save, and wraps the body in a try/catch (logs, doesn't rethrow) since the publisher awaits it
post-commit via `Mode.WaitForAll`.
*Doc impact closed:* `domain-map.md` → Business rules → Completion fan-out now notes any task already
`Cancelled` that day is left untouched rather than overwritten.

### CQ-8 🟠 ✅ FIXED Two events are never published — their handlers are dead code
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
**Resolution:** deleted both dead event/handler pairs (`ActivityAddedToHistoryEvent` —
`ActivityHistory` is already written directly by `DesktopActivityHeartbeatEndpoint` — and
`ActivityCreatedIsOnTodoListEvent`, whose `Activity` has no `IsOnTodoList`/`TaskPriorityId` data to
ever drive it) and updated `domain-map.md`.

### CQ-9 🟠 ✅ FIXED View-refresh failure surfaces as a 500 *after* the data has committed
`infrastructure/persistence/interceptors/SuggestionPatternRefreshInterceptor.cs:36-45`

The refresh runs in `SavedChangesAsync` — i.e. after commit — with no try/catch. Any failure (42P01
when a view is missing, a `REFRESH CONCURRENTLY` rejection when the view lacks a unique index, a
lock-wait timeout) propagates as an unhandled exception. The client gets a 500 for an operation that
**succeeded and is durably persisted**, and any retry hits a duplicate-save path.
**Fix:** wrap each refresh in its own try/catch, log the view name, let the save result stand.
**Doc impact:** none.
**Resolution:** the interceptor no longer runs the refresh at all — see `PERF-1`/`PERF-2`. It only
marks the view dirty in `ISuggestionPatternRefreshQueue`; the new `SuggestionPatternRefreshJob`
(Quartz, `[DisallowConcurrentExecution]`, every 10s) drains the queue and wraps each view's refresh in
its own try/catch, logging view name + elapsed time on both success and failure. A refresh failure can
no longer reach the client at all, since it now runs on a background job thread, not the request.

### CQ-10 🟠 ✅ FIXED Stale refresh flags survive a failed save
`infrastructure/persistence/interceptors/SuggestionPatternRefreshInterceptor.cs:23-25,47-49`

`_refreshPlanner`/`_refreshHistory`/`_refreshTemplate` are set in `SavingChangesAsync` and cleared
only at the end of `SavedChangesAsync`. There is no `SaveChangesFailedAsync` override, so if a save
throws in between, the flags stay `true` and the **next** save on that scoped context — even one
touching none of the three types — triggers a spurious full view refresh.
**Fix:** override `SaveChangesFailedAsync` to reset the flags. **Doc impact:** none.
**Resolution:** added a `SaveChangesFailedAsync` override that resets all three flags.

### CQ-11 🟠 ✅ FIXED No compensation when a post-commit reminder cancel fails
`application/service/reminder/ReminderRegistrationService.cs:120-126`

`DeleteReminderEndpoint.AfterSave` / `DeletePlannerTaskEndpoint.AfterSave` cancel *after* the portal
delete has committed — deliberately, to avoid publishing against a nonexistent row. But there is no
try/catch, retry or outbox: if `registry.CancelAsync` throws, the portal row is gone while the
module's `ReminderDefinition` survives and keeps firing, now referencing a deleted id.

This produces precisely the orphaned-reminder class of bug the class's own doc comments say the
design exists to prevent. Confidence: Med.
**Fix:** catch and log distinctly, and/or queue a reconciliation retry.
**Doc impact:** none.
**Resolution:** `CancelAsync` now wraps `registry.CancelAsync` in a try/catch (excluding
`OperationCanceledException`), logging a loud, distinct error identifying the orphaned
`ReminderDefinition` instead of throwing back into a request that has nothing left to roll back.
`CancelManyAsync` inherits the fix since it calls `CancelAsync` per id, so one failure no longer
aborts the remaining cancels in the batch (also closes half of `PERF-3`).

---

## 9. Performance hotspots

| Where | Issue | Impact | Mitigation | ID |
|---|---|---|---|---|
| `SuggestionPatternRefreshInterceptor.cs:35-45` | `REFRESH MATERIALIZED VIEW CONCURRENTLY` runs **synchronously on the request thread** for every save touching `PlannerTask`/`ActivityHistory`/`Calendar` | O(view size), not O(change size) — a one-row edit pays for a full pattern rebuild. Direct p99 inflation on the three hottest entities | Mark views dirty; refresh on an interval via `Sydowwe.Scheduler` | `PERF-1` 🟠 ✅ FIXED |
| `SuggestionPatternRefreshInterceptor.cs:36-45` | `REFRESH CONCURRENTLY` on one view **serializes** against a second concurrent refresh of the same view | Under normal concurrent planner writes, requests queue behind each other's refresh → thread/connection-pool pressure, not just latency | Same fix as `PERF-1` | `PERF-2` 🟠 ✅ FIXED |
| `ReminderRegistrationService.cs:122-139` | `CancelManyAsync` / `SyncForPlannerTasksAsync` loop with sequential `await`, each iteration doing its own registry round trip **plus** its own `PlannerTasks` query and possible `SaveChangesAsync` | N+1-shaped; a batch planner-task delete multiplies into N sequential DB + module round trips, and a mid-loop failure leaves an undefined subset processed | Batch the task/timezone lookups once per call; bulk registry ops if the contract allows | `PERF-3` 🟠 (partial — DB side now fully batched, registry side blocked. `SyncForPlannerTasksAsync` batch-loads every attached `PlannerTask` in one query, and now the owners' **time zones** too: `SyncCoreAsync` was still calling `ComposeTaskInstantAsync` per reminder, which did its own `User.Timezone` lookup each time — an N+1 the method's own doc comment already claimed was batched. Split into `ResolveTimezoneAsync` + a pure `ComposeTaskInstant`, with the batch caller passing a prefetched zone. A failed cancel no longer aborts the remaining iterations (see `CQ-11`). **Still open:** N sequential registry round trips per call — `IReminderRegistry` (`Sydowwe.Framework.Contracts`) exposes only single-key `RegisterAsync`/`CancelAsync`/`PauseAsync`/`ResumeAsync`, so batching needs a contract change in the submodule affecting every host, not a portal edit. The per-iteration `SaveChangesAsync` was left in place on purpose: hoisting it to one save after the loop means a failure loses *every* `RemindAt` cache update in the batch rather than one, and the registry — not this column — is what actually fires the reminder) |
| `GoogleCalendarService.cs:49-58` | New `GoogleAuthorizationCodeFlow` + `CalendarService` (and underlying `HttpClient`) per call, never disposed, on an `ISingletonService` | Socket exhaustion / per-call TLS handshake under load; undisposed `IDisposable`s | Cache the flow (no per-user state); dispose the `CalendarService` | `PERF-4` 🟡 ✅ FIXED (`GoogleCalendarService.cs`) — flow is now a `Lazy<GoogleAuthorizationCodeFlow>` field built once per singleton instance; `SyncCalendarToGoogleEndpoint` now does `using var calendarService = googleCalendarService.GetCalendarService(...)` so it's disposed after each sync. `GoogleSignInService.cs` (below) is unchanged — separate file, own findings fragment. |
| `GoogleSignInService.cs:17-25` | Same pattern — per-request flow, never disposed | Handle/socket leak on the sign-in path | `using`, or inject a shared client | `PERF-4` 🟡 ✅ FIXED — `flow` is now `using var flow = new GoogleAuthorizationCodeFlow(...)`, so it's disposed at the end of every call. |
| `Program.cs:128` + `AppDbContext.cs:157-160` | Duplicate unconditional EF console logging | Every SQL statement serialized and written to stdout **twice**, in Production | Delete the override; gate the other | `PERF-5` 🟡 ✅ FIXED (`Program.cs` side gated behind `isDevelopment`; `AppDbContext.cs`'s `OnConfiguring` override deleted entirely) |
| `Program.cs:414` | `reader.ReadToEndAsync().Result` — sync-over-async inside the Serilog enrichment delegate, on every non-GET request | Blocks a threadpool thread on the hot write path; contributes to starvation under load | Removed along with `SEC-1`'s fix | `PERF-6` 🟡 ✅ FIXED |
| `ReminderRegistrationService.cs:54-56` | `PlannerTasks` lookup is not `AsNoTracking` despite being read-only | Change-tracker overhead on a hot per-reminder path | `.AsNoTracking()` | `PERF-7` 🟡 ✅ FIXED |
| `RoutinePeriodNudgeJob.cs:49-69` | Notifications sent sequentially | Lengthens the daily sweep as period count grows; low priority at daily cadence | Bounded `Task.WhenAll` | `PERF-8` 🟡 ✅ CLOSED — won't fix, and the suggested mitigation is unsafe here. `INotificationService` writes notification rows through the **same scoped `AppDbContext`** the job holds, so a `Task.WhenAll` fan-out would use one `DbContext` concurrently and throw rather than go faster. Doing it correctly needs a scope (and `DbContext`) per period plus moving the `EndingSoonNotifiedFor`/`GraceNotifiedFor` writes off these tracked entities — not worth it for a once-a-day sweep. Rationale comment added in the job |

**`PERF-1`/`PERF-2` resolution:** the interceptor no longer refreshes anything itself — it marks the
touched view dirty in a new singleton `ISuggestionPatternRefreshQueue`
(`infrastructure/persistence/interceptors/SuggestionPatternRefreshQueue.cs`). A new
`SuggestionPatternRefreshJob` (`infrastructure/jobs/`, Quartz, `[DisallowConcurrentExecution]`, 10s
interval trigger) drains the queue and runs the `REFRESH MATERIALIZED VIEW CONCURRENTLY` calls off the
request thread. `DisallowConcurrentExecution` means only one refresh pass is ever in flight, so two
saves landing seconds apart no longer contend on the same view's refresh lock, and several saves
inside one 10s window coalesce into a single refresh per view instead of one each. Went with a Quartz
job (matching `PurgeExpiredActivityTrackingEntriesJob`'s existing pattern in this portal) rather than
routing through `Sydowwe.Scheduler` as the original mitigation suggested, since these views are
portal-specific and adding a cross-module scheduler contract for them wasn't warranted.

### Indexes that should exist

Derived from query shapes in the reviewed files. **Unverified** — the entity-configuration fan-out
died before covering most of these, so some may already exist.

| Table | Index | Why | ID |
|---|---|---|---|
| `reminder` | `(user_id, remind_at)` | The day view filters `RemindAt` by the user's local-day range; without it this is a per-user range scan that degrades as reminder volume grows | `PERF-9` 🟡 ✅ Verified — `ReminderConfiguration.cs:39` already has `HasIndex(r => new { r.UserId, r.RemindAt })` |
| `activity_history` | `(user_id, start_timestamp)` | History dashboards are user + date-range scans; also backs `mv_activity_history_pattern` | `PERF-10` 🟡 ✅ FIXED — the existing `(UserId, ActivityId, StartTimestamp)` unique index could not serve this: `ActivityId` sits *between* the two columns a user+date-range scan filters on, so Postgres could only use the `UserId` prefix. Added `builder.HasIndex(a => new { a.UserId, a.StartTimestamp })` to `ActivityHistoryConfiguration`. Needs a migration |
| `planner_task` | `(user_id, calendar_id)` / date-range | Day view is the SPA's main read | `PERF-11` 🟡 ✅ Verified — `PlannerTaskConfiguration` has `(UserId, CalendarId, StartTime)`, a superset of what was suggested |
| `routine_period_completion` | `(time_period_id, period_start desc)` | Streak history lookups bounded by `HistoryDepth` | `PERF-12` 🟡 ✅ Verified — `RoutinePeriodCompletionConfiguration` has `(TimePeriodId, PeriodStart)` unique. Ascending rather than `desc`, which is fine: Postgres scans a btree backwards at the same cost |
| `refresh_token` | token-lookup column; expiry column | Every refresh does the first; `RefreshTokenCleanupService` sweeps on the second | `PERF-13` 🟡 ✅ Verified — `RefreshTokenConfiguration` has `TokenHash`, `ExpiresAt` and `(UserId, IsRevoked)`; both suggested indexes exist |

---

## Code/doc drift

Checked against `AdhdTimeOrganizer/docs/domain-map.md`, which landed after the fan-out started.

### DOC-1 🟠 ✅ FIXED `PortalEndpointHelper` does not exist
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
**Resolution:** confirmed endpoints call `IEndpoint.GetUserRole()` / `GetAdminRole()` directly (no
wrapper referenced anywhere) — the doc was simply stale. Removed the `PortalEndpointHelper` row from
`domain-map.md`'s Navigation index, and reworded both CLAUDE.md paragraphs that described it (the
"portal copies are now `PortalEndpointHelper`/`TimeOnlyExtensions`" line now names only
`TimeOnlyExtensions`; the role-arrays paragraph now says to call `IEndpoint.GetUserRole()`/
`GetAdminRole()` directly, with no portal wrapper).

### DOC-2 🟠 ✅ FIXED Two events documented as wired are never published
`domain-map.md:280-283` lists `ActivityAddedToHistoryEvent` and `ActivityCreatedIsOnTodoListEvent`
among the live events, explicitly separating out only two others as "declared-but-unhandled". Per
`CQ-8`, neither of these two is ever published either — they are handled but never raised, which is
the mirror-image dead state the doc doesn't have a category for.
**Which side is wrong:** the code, if the features are wanted; otherwise both. **Doc impact:**
`domain-map.md` → Events (either remove them or mark them "handled but never published").
**Resolution:** both event/handler pairs deleted (see `CQ-8`); `domain-map.md` updated to drop them
from the Events line with a note on why.

### DOC-3 🟠 ✅ FIXED "all items and their steps are unticked" — steps are not unticked
`domain-map.md:146` states the reset unticks items *and their steps*. Per `CQ-3` the job's query
never loads `Steps`, so the step-reset loop is a no-op.
**Which side is wrong:** the code. **Doc impact:** none once `CQ-3` is fixed — the doc describes the
intended behavior correctly.
**Resolution:** `CQ-3` fixed (query now includes `Steps`); no doc edit was needed.

### DOC-4 🟠 ✅ FIXED `CheckGrace` "meant to be called before any reset evaluation" — its result is discarded
`domain-map.md:147-148`. Per `CQ-2`, `CheckGrace` *is* called first but its mutations are dropped
whenever no period is due for reset.
**Which side is wrong:** the code. **Doc impact:** none once `CQ-2` is fixed.
**Resolution:** `CQ-2` fixed (`graceChanged` accumulator gates the early return); no doc edit was
needed. Also added an explicit domain-map.md note on which `TryReset` overload may advance
`LastResetAt` (see `CQ-4`'s doc-impact resolution below).

### DOC-5 🟠 ✅ FIXED "`DoneCount` is snapped … for step-counted items" — the steps themselves are not
`domain-map.md:159`. Per `CQ-6` the counts are snapped but `Steps` is neither loaded nor updated,
leaving the two representations of the same state contradicting each other.
**Which side is wrong:** the code. **Doc impact:** none once `CQ-6` is fixed.

### DOC-6 🟡 ✅ VERIFIED `RoutineTimePeriod`'s two unique indexes — seeder compliance unverified
`domain-map.md:63,67` documents **two** per-user unique indexes: `(UserId, Text)` and
`(UserId, LengthInDays)`. CLAUDE.md warns that `RoutineTimePeriodSeeder`'s `Collides` must cover
**both** or a latent 23505 ships.

The agent assigned to that seeder was killed by the session limit, so this is **unverified**. Flagged
here so it is not lost — it is a concrete, cheap check.
**Which side is wrong:** unknown. **Doc impact:** none (the doc is believed correct).
**Resolution:** verified directly — `RoutineTimePeriodSeeder.Collides` (line 29-30) is
`a.Text == b.Text || a.LengthInDays == b.LengthInDays`, covering both unique indexes with an `OR`
(either one is sufficient to reject a row). No doc edit needed; the doc was correct.

---

## 🟡 Nits appendix

Grouped; each is one line from a fragment. Full detail in `findings/`.

**Correctness-adjacent**
- `CQ-12` ✅ FIXED `RoutineResetService.cs:34-50` — monthly/yearly `ComputeNextReset` always steps exactly one
  period, never catching up to `now`. A dormant routine manufactures one fabricated
  `RoutinePeriodCompletion` (and streak transition) per missed cycle, from current item state. Both
  branches now loop forward (by month/year) until the candidate reset is `>= now` instead of stepping
  once; `ComputeNextReset` gained an explicit `now` parameter (all three call sites updated) so the
  loop bound doesn't rely on ambient time.
- `CQ-13` ✅ FIXED `RoutineResetService.cs:15,69` — weekly path resets at 00:00 UTC, monthly/yearly at 02:00
  UTC. Undocumented asymmetry that will bite the next nudge-window feature. Unified on midnight UTC
  for every branch via a shared `MonthlyCandidate` helper.
- `CQ-14` ✅ FIXED `BasePlannerTask.cs:7-9` — `IsNextDay` commented out; `TimeOnly` start/end cannot express an
  overnight task, so duration goes negative. Inherited by all three planner-task types. Every
  Start/EndTime validator (`PlannerTaskValidator`, `RepeatingPlannerTaskValidator`,
  `TemplatePlannerTaskValidator`, `PlannerTaskChangeSpanValidator`, `ApplyTemplateToTaskPlannerValidator`)
  already rejects `EndTime <= StartTime`, so overnight tasks are genuinely out of scope today — deleted
  the dead comment rather than half-building unrequested overnight support (which would also need
  every one of those validators changed).
- `CQ-15` ✅ FIXED `BasePlannerTask.cs:19` — `IsOptional` is `Importance?.Importance == 666`, an unnamed
  sentinel. (`domain-map.md` documents it, so the doc is right; the code wants a constant.) Added
  `TaskImportance.OptionalMarkerValue`/`CriticalMarkerValue` constants and used them here and in the
  three seeders that previously hardcoded `666`/`999`.
- `CQ-16` ✅ FIXED `TaskPlannerHelper.cs:18` — `TasksOverlap` assumes same-day non-wrapping intervals; pairs
  with `CQ-14`. Added a comment documenting the invariant (every planner-task validator already
  rejects `EndTime <= StartTime`, per `CQ-14`'s resolution) rather than adding unrequested
  wrap-around handling.
- `CQ-17` ✅ FIXED `Activity.cs:39-44` — `Clone()` uses `MemberwiseClone`, sharing navigation-collection
  *references* with the source. Safe only because the sole caller fetches via `FindAsync` with no
  `Include`. `Clone()` now nulls/resets `BacklogProfile`/`ProjectProfile`/`BucketListProfile`/
  `MemoryAnchors` on the clone after `MemberwiseClone`, so future callers that `Include` navigations
  no longer inherit shared references.
- `CQ-18` ✅ FIXED `BaseTodoListItem.cs:9-14` — `DoneCount`/`TotalCount` invariant lived only in a DB check
  constraint and was re-derived in two handlers; `Steps` was a publicly settable collection. Added
  `BaseTodoListItem.SetDone(bool)`, which snaps `IsDone`/`DoneCount`/`Steps` together in one place, and
  switched `PlannerTaskIsDoneChangedEventHandler` and `RoutineResetService.TryReset` (both overloads) —
  the two duplicated call sites — onto it. Left `Steps` publicly settable: making it read-only would
  need a broader DTO-mapping change that conflicts with this codebase's established convention (see
  `CQ-14`/`CQ-16`'s resolutions) and is out of scope for this fix.
- `CQ-19` ✅ FIXED `TodoListEntityConfigurationExtensions.cs:16` — the `done_count <= total_count` check is
  bypassed when either column is NULL (Postgres treats NULL checks as satisfied). Made the
  null-independence explicit: `done_count IS NULL OR total_count IS NULL OR done_count <= total_count`.
  Functionally a no-op (Postgres already passed on NULL), but self-documents the intended nullable
  behavior. Added migration `20260810092732_FixTodoListDoneCountCheckConstraint` (drops/re-adds both
  the `todo_list_item` and `routine_todo_list` constraints).
- `CQ-20` ✅ FIXED `GoogleSignInService.cs:66-69` — catch-all maps tampered/expired tokens to
  `InternalServerError`; client errors reported as 5xx pollute alerting. Added a `catch (Exception ex)
  when (ex is TokenResponseException or InvalidJwtException)` clause ahead of the generic catch,
  mapping a rejected code exchange or a tampered/invalid ID token to `BadRequest`; the generic catch
  still covers genuinely unexpected failures as `InternalServerError`. Also fixed in the same pass
  (not separately tracked): the per-call `GoogleAuthorizationCodeFlow` is now `using`-disposed
  (folds into `PERF-4`), and `GoogleUserInfo.Name`/`.Picture`/`.Locale` are now `string?` since Google
  doesn't guarantee those OIDC claims.
- `CQ-21` ✅ FIXED `Program.cs:259-274` — `pageUrl` is added to CORS `origins` unconditionally and is `null`
  when `PAGE_URL` is unset; `WithOrigins` on a null entry throws or silently misconfigures. Now only
  added when non-empty, same as the extension-id check right below it.
- `CQ-22` ✅ FIXED `SeedUserIdProvider.cs:40` — `Helper.GetEnvVar("ROOT_ADMIN_EMAIL")` throws when unset, but
  the caller only handles the null case; a missing `.env` entry crashes dev seeding instead of
  skipping gracefully. Switched to `Environment.GetEnvironmentVariable`, returning `null` when unset
  or empty so a missing env var and "no root admin yet" both resolve the same way.
- `CQ-23` ✅ FIXED `EntityWithActivityBuilderExtensions.cs:11-26` — `isRequired` is caller-settable against a
  non-nullable `long ActivityId`; `isRequired: false` either fights the CLR type or is silently ignored.
  Removed the parameter from both methods (now always `.IsRequired()`); all seven call sites already
  used the default, so this is a no-op behavior change. Also fixed the fragment's nit in the same pass:
  the class now uses the C# 13 `extension<TEntity>(...)` member syntax and returns
  `ReferenceCollectionBuilder`/`ReferenceReferenceBuilder` for fluent chaining, matching the sibling
  `EntityWithUserBuilderExtensions`.
- `CQ-24` ✅ FIXED `Reminder.cs:50` — `LeadOffsetsMinutes` "≤ 0 and unique" was enforced only by the
  validator and the module registry, never by the entity — any write path bypassing the DTO
  validator (seeder, script, future endpoint) could persist a positive or duplicate offset. Turned
  `LeadOffsetsMinutes` into a validated property (backing field + setter) that throws `ArgumentException`
  on a positive or duplicate value, as defense in depth behind `ReminderValidator`. No CHECK constraint —
  the column is `jsonb`, which Postgres can't constrain element-wise as cheaply as the CLR guard.
- `CQ-25` `Reminder.cs:47` — doc says UTC but the type is plain `DateTime` with no `Kind` guarantee.
  Not changed: the write boundary (`ReminderRegistrationService.AsUtc`) already pins any incoming
  `Kind` (`Utc`/`Local`/`Unspecified`) to UTC before use, so this is already defended in practice: a
  `DateTimeOffset` migration would be a schema change for a gap that's already closed at the one
  write path that matters.
- `CQ-26` ✅ FIXED `DesktopActivityEntry.cs:8-9` — nothing enforced `RecordDate == DateOnly.FromDateTime(WindowStart)`,
  so a row could be misfiled into the wrong partition. `RecordDate` is now derived automatically inside
  `WindowStart`'s `init` accessor (private setter), so the two can no longer diverge; the one call site
  (`DesktopActivityHeartbeatEndpoint`) no longer sets `RecordDate` explicitly.
- `CQ-27` ✅ FIXED `WebExtensionActivityEntry.cs:8` — "Always 1-min aligned" was an unenforced comment, and the
  invariant is load-bearing: `WebExtensionTimelineEndpoint` derives each window's end as
  `WindowStart.AddMinutes(1)` and stitches adjacent windows by testing `WindowStart == previous.EndedAt`,
  so an unaligned value yields gapped/overlapping timeline segments silently rather than an error.
  `WebExtensionHeartbeatValidator` pinned `WindowMinutes == 1` but never checked `WindowStart` itself
  (not even `NotEmpty`). Added a validator rule (`Ticks % TimeSpan.TicksPerMinute == 0`) so a
  misbehaving client gets a 400, and replaced the bare comment with a doc comment stating the invariant
  and where it is enforced. Deliberately **not** a throwing setter on the entity — EF would run it while
  materializing rows written before the rule existed.
  Also applied the `CQ-26` treatment in the same pass: `RecordDate` is now derived inside `WindowStart`'s
  `init` accessor (private setter) instead of being set by callers, so the partition key can no longer
  diverge from the window it partitions. The four call sites (heartbeat endpoint + three in
  `WebExtensionDataSeeder`) no longer set it explicitly.

**Consistency / hygiene**
- `CQ-28` ✅ FIXED `AppCommandDbContextFactory.cs:16` — no `MigrationsAssembly` override, unlike `Program.cs`.
  Resolves identically today; asymmetric by inspection. Added the same `MigrationsAssembly` override.
- `CQ-29` ✅ FIXED `DefaultUsersSeeder.cs:59` — hard-coded `"Root"` instead of `nameof(UserRoleEnum.Root)`.
  A typo would silently produce a rootless admin with no compile-time signal. Now uses
  `nameof(UserRoleEnum.Root)`.
- `CQ-30` ✅ FIXED `DefaultUsersSeeder.cs:34-46` — the `overrideData` branch sits outside the try/catch that
  covers the create path, and discards `IdentityResult.Succeeded` from `UpdateAsync`/`ResetPasswordAsync`.
  Wrapped the override branch in its own try/catch and now logs (fixed, non-PII messages) when either
  `UpdateAsync` or `ResetPasswordAsync` fails.
- `CQ-31` ✅ FIXED `UserDefaultsService.cs:20` and `RoutinePeriodNotificationService.cs:76` — `catch (Exception)`
  swallows `OperationCanceledException`, reporting shutdown/disconnect as a failure. Both now add
  `catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }` above the generic
  catch.
- `CQ-32` ✅ MOOT `ActivityAddedToHistoryEventHandler.cs:26` — `logger.LogError(result.ErrorMessage)` passes a
  runtime string as the message template; braces in it would be parsed as Serilog holes. The file was
  deleted under `CQ-8` (verified gone), so there is nothing left to fix here. Note the *pattern* may
  well exist elsewhere in the 670 unreviewed portal files — this closes the one instance, not the class.
- `CQ-33` ✅ FIXED (all 3) `Program.cs:167-176,199-203,372-373` — three pieces of TEMP debug scaffolding
  left in the composition root: a `StartNow()` trigger that fired `RoutineTodoListResetJob` on **every
  boot** (✅ removed), `Console.WriteLine` stack-trace dumps on `ApplicationStopping` (✅ removed,
  `logger.LogInformation` kept), and the `ValidationSchemaProcessor` removal that degrades dev Swagger
  (✅ removed 2026-08-13 — root cause below).

  **Root cause: our own registration order, not an upstream bug.** The earlier diagnosis (a
  self-referential schema defeating `ApplyRulesToSchema`'s `HashSet<Type>` guard) described the
  mechanism correctly but blamed the wrong party, so the hunt for "the culprit validator" was chasing
  something that does not exist. What actually fed the recursion was
  `ICreateRequest<TEntity>.ToEntity` — the get-only mapping property that pulls the raw, cyclic EF
  navigation graph into the schema. `RemoveToEntitySchemaProcessor` exists precisely to strip it and
  *was* registered, but with `SchemaProcessors.Add(...)`: FastEndpoints registers its own
  `ValidationSchemaProcessor` inside `EnableFastEndpoints` and only **afterwards** invokes the host's
  `DocumentSettings` action, so appending placed our stripper **behind** the validation processor. It
  ran too late, every time. That is why the overflow persisted "even with `RemoveToEntitySchemaProcessor`
  active", and why 8.2.0 changed nothing — the version was never the variable.

  **Fix:** `RemoveToEntitySchemaProcessor.PrependTo(...)` rebuilds the collection so the stripper is
  first (`SchemaProcessors` is an `ICollection` with no indexer, so prepending means rebuilding);
  the `ValidationSchemaProcessor` removal block is deleted. Verified live on 8.1.0:
  `/swagger/v1/swagger.json` returns **200** (774 KB, 240 paths, 461 schemas), `/swagger/index.html`
  returns 200, the process survives, and `toEntity` appears **0** times in the document. Processor order
  at runtime confirmed as `RemoveToEntitySchemaProcessor | ValidationSchemaProcessor |
  PolymorphismSchemaProcessor`. **No package change and no DTO/validator reshaping was needed**, so the
  8.2.0 revert stands on its own merits and the FastEndpoints pins stay at 8.1.0. No upstream issue is
  warranted.

  Guard: `AdhdTimeOrganizer.IntegrationTests/Infrastructure/SwaggerSchemaProcessorOrderTests.cs`
  (3 tests, no DB). It asserts the ordering invariant rather than regenerating the document, for two
  reasons: Swagger is registered only under Development, which the test host is not; and a
  `StackOverflowException` cannot be caught or contained, so a test that regenerated the document would
  take the entire xunit run down with it and report nothing on regression.

  ⚠ **Follow-up, separate from this finding — the validation processor contributes nothing today.**
  With ordering fixed and `ValidationSchemaProcessor` live, the generated document is **byte-identical**
  (SHA-256 match) to one generated with that processor removed, even though 91 endpoints have a
  validator attached. Every length/range constraint in the document traces to a
  `[StringLength]` / `[MaxLength]` / `[Range]` data annotation — the `minLength: 0` companions are the
  `[StringLength]` signature, and no property carries the `minLength: 1` that an unconditional
  FluentValidation `NotEmpty()` would produce. So the stated cost of the old workaround ("dev Swagger
  loses FluentValidation-derived constraints") was never actually being paid. Part of the explanation is
  by design — FastEndpoints skips rules with a `When(...)` condition, and most validators here are
  conditional — but `PomodoroTimerPresetValidator.Name` is unconditional `NotEmpty().MaximumLength(255)`
  and still does not land. Unverified hypothesis: a property-name casing mismatch (the document emits
  **PascalCase** property names while the processor looks rules up by its own naming policy). Worth its
  own finding if the FluentValidation constraints are actually wanted in dev Swagger; keeping the
  processor enabled costs nothing either way.
- `CQ-34` ✅ FIXED `SuggestionPatternViewInstaller.cs:30-31` — resource matching uses `Contains` rather than an
  anchored prefix, and creation order is alphabetical-by-resource-name with no explicit dependency
  declaration. Resource matching now anchors with `StartsWith($"{assembly.GetName().Name}{ResourceFolder}")`;
  the alphabetical/independent-views ordering assumption is now stated explicitly in the class remarks
  rather than left implicit. Also fixed in the same pass (not separately tracked): the check-then-create
  sequence is now wrapped in a session-level `pg_advisory_lock`/`pg_advisory_unlock` pair around an
  explicitly opened connection, so two instances booting concurrently serialize instead of both racing
  to create the same materialized view and one crashing on a duplicate-object error.
- `CQ-35` ✅ FIXED `TodoListExtensions.cs:22,27` — parameter named `timePeriodId` is actually filtered against
  `TaskPriorityId` on the `TodoListItem` overload. Renamed to `taskPriorityId` on that overload.
- `CQ-36` ✅ FIXED `SerilogConfig.cs:54,57-71` — production detection reads the raw `ASPNETCORE_ENVIRONMENT`
  env var instead of `context.HostingEnvironment.IsProduction()`; the table is named `warning_logs`
  but its minimum level is `Information`; `WriteTo.PostgreSQL` is called with 11 positional args.
  Switched to `context.HostingEnvironment.IsProduction()`; renamed the table to `app_logs`; converted
  the `WriteTo.PostgreSQL` call to named arguments (verified against the actual 4.2.0 package signature
  via `ilspycmd`, since the positional order didn't match any documented overload — `useCopy: false`
  and `schemaName: "command"` are the real, previously-inscrutable values at those positions, left
  unchanged). Also added a 90-day `retentionTime` (closes the retention half of `SEC-1`) and a comment
  clarifying that the commented-out `user_agent`/`client_ip`/`auth_method`/`user_id`/`role` columns are
  still captured via the `properties` JSONB writer regardless.
- `CQ-37` ✅ FIXED File/class naming mismatches: `ActivityCreatedIsOnToDoListEventHandler.cs` (capital D) vs
  `ActivityCreatedIsOnTodoListEventHandler` (lowercase) — moot, both files were deleted under `CQ-8`.
  `NotificationsUserFkConfiguration.cs` held three differently-named top-level classes (`Notification`,
  `NotificationPreference`, `PushSubscription` FK configs) — renamed to
  `NotificationsModuleUserFkConfigurations.cs` to signal it's a multi-class file; nothing referenced
  the old filename.
- `CQ-38` ✅ FIXED `User.cs:28-32` — `PhoneNumber`/`PhoneNumberConfirmed` re-override `[NotMapped]` already
  declared identically on `BaseUser`. Removed the redundant overrides — `BaseUser`'s already apply.

**Clean files** — no issues found: `PortalRoleCatalog.cs`, `EntityWithUserBuilderExtensions.cs`
(verified against the framework target: `IsRequired()` + `Cascade` both preserved, so no GDPR erasure
gap), `BaseEntityWithUser.cs` and `BaseLookupWithUser.cs` (both correctly plain closing types),
`PortalAuthorizationPolicies.cs`, `DependencyInjectionExtensions.cs` (the required
`Except(ModuleAssemblies)` guard is present), `ModuleServiceExtensions.cs`.