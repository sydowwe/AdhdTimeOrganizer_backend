# Portal review — 03 · Risks & rollout

> ⚠ **Scope:** 42 of 712 portal files (~6%). The endpoint layer — where user-scoping and IDOR risk
> actually concentrates — was **not reviewed**. Read the ranking below as "worst of what was seen",
> not "worst in the portal". See `00-STATUS.md`.

Detail for every ID lives in `02-findings.md`; this file does not restate it.

## 11. Risk-ranked issue list

| # | Sev | Issue | ID | File |
|---|---|---|---|---|
| 1 | 🔴 | Plaintext passwords + PII from every non-GET request body persisted to the Production log DB, unredacted and unpurged | `SEC-1` | `Program.cs:409-419`, `config/SerilogConfig.cs:28` |
| 2 | 🟠 | Google Calendar OAuth **refresh token** stored in plaintext — a long-lived third-party credential | `SEC-2` | `domain/model/entity/user/User.cs:21` |
| 3 | 🟠 | Desktop window titles (document names, chat/email subjects) stored plaintext with no retention purge | `SEC-4` | `…/activityTracking/desktop/DesktopActivityEntry.cs:12` |
| 4 | 🟠 | Full browsing history retained indefinitely; the query filter hides old rows without deleting them | `SEC-5` | `…/activityTracking/WebExtensionActivityEntry.cs:9-10` |
| 5 | 🟠 | Every SQL statement + parameter values logged to stdout twice, in Production | `SEC-3` | `Program.cs:128`, `AppDbContext.cs:157-160` |
| 6 | 🟠 | `REFRESH MATERIALIZED VIEW CONCURRENTLY` runs synchronously on the request thread for every planner/history/calendar save | `PERF-1` | `SuggestionPatternRefreshInterceptor.cs:35-45` |
| 7 | 🟠 | …and serializes against concurrent refreshes of the same view, queueing requests | `PERF-2` | `SuggestionPatternRefreshInterceptor.cs:36-45` |
| 8 | 🟠 | Routine reset silently discards grace-expiry streak breaks whenever no period is due that day | `CQ-2` | `RoutineTodoListResetJob.cs:43-47` |
| 9 | 🟠 | Routine reset never unticks checklist steps — the `Steps` include is missing, so the reset loop is a no-op | `CQ-3` | `RoutineTodoListResetJob.cs:21-23` |
| 10 | 🟠 | Two `TryReset` overloads disagree on streak scoring; a step toggle can consume a cycle and lose its outcome permanently | `CQ-4` | `domain/service/RoutineResetService.cs:135-150` |
| 11 | 🟠 | One failing notification aborts the nudge sweep and loses every already-sent idempotency marker → duplicate nudges | `CQ-5` | `RoutinePeriodNudgeJob.cs:49-75` |
| 12 | 🟠 | To-do fan-out silently un-cancels deliberately cancelled tasks, skips reminder sync, leaves stale actual times | `CQ-7` | `TodoListItemIsDoneChangedEventHandler.cs:27-28` |
| 13 | 🟠 | Planner fan-out snaps counts but not steps, desyncing the checklist from `DoneCount` | `CQ-6` | `PlannerTaskIsDoneChangedEventHandler.cs:30-42,56-67` |
| 14 | 🟠 | Two events are never published anywhere — "activity is on to-do list" silently does nothing | `CQ-8` | `application/eventHandler/` (2 files) |
| 15 | 🟠 | View-refresh failure throws *after* commit → 500 for an operation that succeeded | `CQ-9` | `SuggestionPatternRefreshInterceptor.cs:36-45` |
| 16 | 🟠 | Stale refresh flags survive a failed save, triggering spurious full refreshes on the next unrelated save | `CQ-10` | `SuggestionPatternRefreshInterceptor.cs:23-25,47-49` |
| 17 | 🟠 | Failed root-admin creation falls through to role assignment and default-seeding on user id 0 | `CQ-1` | `DefaultUsersSeeder.cs:56-66` |
| 18 | 🟠 | No compensation when a post-commit reminder cancel fails → orphaned reminder keeps firing | `CQ-11` | `ReminderRegistrationService.cs:120-126` |
| 19 | 🟠 | N+1 sequential registry + DB round trips on reminder batch operations | `PERF-3` | `ReminderRegistrationService.cs:122-139` |
| 20 | 🟠 | `PortalEndpointHelper` is documented in two places but does not exist | `DOC-1` | *(absent)* `application/helper/` |
| 21 | 🟠 | Docs assert two events are wired that are never raised | `DOC-2` | `docs/domain-map.md:280-283` |
| 22 | 🟠 | Docs assert steps are unticked at reset / snapped in fan-out / grace persisted — none hold | `DOC-3`,`DOC-4`,`DOC-5` | `docs/domain-map.md:146-148,159` |

🟡 items (`SEC-6`–`SEC-14`, `CQ-12`–`CQ-38`, `PERF-4`–`PERF-13`, `DOC-6`) are in the Nits appendix of
`02-findings.md` and are not ranked here.

**Reading the ranking.** Items 1–5 are data-protection exposures — they are what a regulator or a
breach would surface, and they are cheap to fix. Items 6–7 are the only findings that degrade under
*normal* production load rather than at an edge. Items 8–13 are a cluster: the routine/completion
subsystem is the least-correct area of the reviewed code, and `01-testing.md` shows it has the least
test coverage. That correlation is the actionable signal in this review.

## 10. Migration / rollout risks

| Risk | Likelihood | Mitigation | ID |
|---|---|---|---|
| **Encrypting `GoogleCalendarRefreshToken` (`SEC-2`) needs a data migration, not just a config change.** Existing rows hold plaintext; `EncryptedColumn` stores a versioned token. A naive column swap leaves old rows undecryptable and silently breaks every existing Calendar connection | High if attempted as a one-liner | Ship the encrypted column alongside, backfill by re-encrypting in a data migration, then cut over. Or accept forced reconnection and null the column — Calendar sync is opt-in, so blast radius is small. Requires `FIELD_ENCRYPTION_KEY` present in every environment **before** deploy, or the app fails at first read | `MIG-1` |
| **Encrypting `WindowTitle` (`SEC-4`) conflicts with an existing unique index.** `DesktopActivityEntryConfiguration.cs:31` builds a unique index over `(UserId, WindowStart, RecordDate, ProcessName, WindowTitle)` for ingest idempotency. `EncryptedColumn` is randomized and cannot be indexed — encrypting the column silently destroys heartbeat dedup | High if done mechanically | Add a non-encrypted hash/fingerprint column, move the uniqueness constraint onto it, then encrypt the text. Two migrations, in that order. **Do the retention purge first** — it is independent and delivers most of the privacy benefit | `MIG-2` |
| **`SuggestionPatternViewInstaller` has no locking (`CQ-34` context).** Check-`to_regclass`-then-`CREATE` is not atomic and runs on every boot. Two instances starting concurrently — a rolling deploy, a fast container restart, any multi-replica setup — can both see the view missing; the loser crashes at startup | Medium — invisible in single-instance dev/test, appears on first scale-out | Wrap in `pg_advisory_lock`, or use `CREATE MATERIALIZED VIEW IF NOT EXISTS` (supported since PG 9.3) and treat duplicate-object as benign | `MIG-3` |
| **Drift between the two view-installation paths.** `AppDbContextFixture.OnSchemaCreatedAsync` (file copy via `Content`) and `SuggestionPatternViewInstaller` (embedded resources) independently install the same three scripts. A new script added to only one — or a csproj item type changed — makes tests and runtime disagree, and the failure mode is a 42P01 at save time, not a build error | Medium | Single source: have the fixture call the installer, or add a test asserting both paths see the same script set | `MIG-4` |
| **First retention purge on the tracking ledgers will be a very large delete.** `DesktopActivityEntry` and `WebExtensionActivityEntry` have never been purged and accumulate per-minute heartbeats per user. A naive `ExecuteDeleteAsync` over years of rows will lock and bloat | High, once `SEC-4`/`SEC-5` are actioned | Both tables are `IsPartitionedByRange` — **drop whole partitions** rather than deleting rows. Chunk anything that must be row-wise | `MIG-5` |
| **Partition exhaustion on the two tracking tables.** `IsPartitionedByRange` needs partitions configured ahead of the boundary; when the list runs out, **inserts start failing** at a date rollover — a hard outage of tracking ingest with no prior warning | Unknown — **unverified**, the two configuration agents were killed before reporting | Read `DesktopActivityEntryConfiguration` / `WebExtensionActivityEntryConfiguration`, determine the last configured partition, and diary the extension. Treat as urgent until disproven | `MIG-6` |
| **Adding the unique index `domain-map.md` claims on `RoutinePeriodCompletion(TimePeriodId, PeriodStart)`.** If it does not already exist, `CQ-2`/`CQ-4` mean duplicate completion rows may already be in production data; the `CREATE UNIQUE INDEX` would then fail | Unknown — **unverified**, that configuration agent was killed | Check for the index; if absent, dedupe before creating it. Fix `CQ-4` first or new duplicates keep arriving | `MIG-7` |
| **`RoutineTimePeriodSeeder` may violate the second unique index.** `domain-map.md` documents two per-user unique indexes on `RoutineTimePeriod`; CLAUDE.md warns `Collides` must cover **both**. If it covers only one, sign-up seeding throws 23505 for affected users | Unknown — **unverified** (`DOC-6`) | Read the seeder's `Collides`; `PerUserDefaultMatcherTests` is the right place to pin it | `MIG-8` |
| **Fixing `CQ-3` (step reset) changes live behavior.** Steps that have silently stayed ticked for the lifetime of the feature will all untick on the first run after deploy | Certain, by design | Expected and correct — but it will look like a bug to users mid-routine. Ship with a note, ideally not mid-week | `MIG-9` |
| **Removing the TEMP `StartNow()` trigger (`CQ-33`).** It currently fires `RoutineTodoListResetJob` on every boot; removal is correct, but any behavior that has come to depend on the extra run (e.g. masking `CQ-2` by re-running after a deploy) will stop | Low | Fix `CQ-2` and `CQ-3` **before** removing the trigger, so the daily run is actually sufficient | `MIG-10` |

## Audit-log gaps

Auditing is **not wired** in this solution — `AuditSaveChangesInterceptor` is not registered on
`AppDbContext` (only `SuggestionPatternRefreshInterceptor` is), the audit entity configurations live
in an assembly `AppDbContext` never applies, and there is no `audit_log` migration. Nothing is being
captured today. Everything below is therefore *pre-emptive*, except `AUDIT-1`/`AUDIT-2`/`AUDIT-3`,
which are live retention gaps independent of auditing.

| Gap | Detail | ID |
|---|---|---|
| **No retention purge for `DesktopActivityEntry`** | Append-only per-minute heartbeat ledger holding window titles. CLAUDE.md's Ledger Retention section requires a `RetentionOptions` subclass + purge handler; none exists (searched). GDPR Art. 5(1)(e) | `AUDIT-1` 🟠 |
| **No retention purge for `WebExtensionActivityEntry`** | Same, holding URLs. The `RecordDate >= CurrentPartitionDate` filter suppresses old rows from EF reads only | `AUDIT-2` 🟠 |
| **No retention for the Postgres log sink** | `warning_logs` has no cutoff, no purge job, no partitioning — while the file sink beside it sets `retainedFileCountLimit: 30`. Combined with `SEC-1`, credentials accumulate there forever | `AUDIT-3` 🟠 |
| **`[AuditIgnore]` missing on sensitive properties** | `User.GoogleCalendarRefreshToken`, `User.GoogleOAuthUserId`, `WebExtensionActivityEntry.Url`, `DesktopActivityEntry.WindowTitle`, `Activity.Text`. Harmless today; the moment the interceptor is enabled these are copied verbatim into `audit_log` snapshots and `ChangedProperties` — duplicating the most sensitive data into a second, longer-lived, less-access-controlled store | `AUDIT-4` 🟡 |

**No `ExecuteUpdateAsync` / `ExecuteDeleteAsync` audit-bypass was found in the reviewed 42 files.**
`RoutineTodoListResetJob` was specifically checked and correctly uses tracked entities +
`SaveChangesAsync`, so it will be audited once the interceptor is on and it stamps timestamps
correctly today. This is a genuinely clean result for the reviewed surface — but the retention purge
handlers that `AUDIT-1`/`AUDIT-2` call for **will** need `ExecuteDeleteAsync`, and that is correct
for `[NoAudit]` ledgers. Mark those entities `[NoAudit]` when writing the purges.

**Unreviewed:** the 275 endpoints, 62 validators and most entity configurations. Bulk-update
operations in command endpoints are the most likely place for a real audit bypass, and none of them
were looked at.
