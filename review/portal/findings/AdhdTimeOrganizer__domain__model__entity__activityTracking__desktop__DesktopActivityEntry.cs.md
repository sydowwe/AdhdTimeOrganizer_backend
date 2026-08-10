# Review: AdhdTimeOrganizer/domain/model/entity/activityTracking/desktop/DesktopActivityEntry.cs
Role: entity
Summary: Well-formed partitioned, user-scoped ledger entity, but stores highly sensitive PII (window titles, executable paths) in plaintext with no visible retention/purge job.
Coverage: n/a

## Issues
- [High][Security] DesktopActivityEntry.cs:12 — `WindowTitle` (and `ExecutablePath`) routinely contains document names, chat/email subject lines, URLs, and other free-text PII, but is stored as a plain `string` column with no `EncryptedColumn` and no `[AuditIgnore]`/`[NoAudit]` marker.
  Why: This is exactly the "high-sensitivity string" case CLAUDE.md calls out `EncryptedColumn` for (GDPR Art. 32); combined with the "no PII in logs" rule, plaintext window titles at rest are the single largest privacy exposure in this activity-tracking feature, and if/when auditing is wired up, full window-title history would be captured in audit snapshots by default.
  Fix: Consider `EncryptedColumn(x => x.WindowTitle)` (and possibly `ExecutablePath`) if the product doesn't need to filter/sort/aggregate on raw title text server-side; if auditing is later enabled, add `[AuditIgnore]` to these properties regardless.
  Confidence: Med

- [Medium][Quality] DesktopActivityEntry.cs:12 — `EncryptedColumn` is explicitly incompatible with filtering/sorting/uniqueness per CLAUDE.md, yet the configuration (`DesktopActivityEntryConfiguration.cs:31`) builds a unique index over `WindowTitle` alongside `UserId`/`WindowStart`/`RecordDate`/`ProcessName` — so encrypting this column isn't a drop-in fix without redesigning dedup logic.
  Why: Flags that the encryption suggestion above needs a companion change (e.g. a hashed dedup column) rather than being purely mechanical.
  Fix: If encrypting WindowTitle, add a separate non-encrypted hash/fingerprint column for the uniqueness constraint instead of the raw text.
  Confidence: Med

- [Medium][AuditGap] DesktopActivityEntry.cs — this is an append-only, per-heartbeat activity ledger (per `DesktopActivityHeartbeatEndpoint`) but no purge/retention handler for it was found under `application/` (unlike the Scheduler/Reminders/Notifications ledgers CLAUDE.md documents with explicit `PurgeExpired...JobHandler`s bound to `RetentionOptions`).
  Why: Per CLAUDE.md's Ledger Retention section, append-only ledgers need a retention purge or they grow forever, and this table additionally holds PII (window titles), making unbounded retention a GDPR Art. 5(1)(e) concern, not just a storage-growth one.
  Fix: Add a `SectionName`-scoped `RetentionOptions` subclass and a purge job handler for `DesktopActivityEntry` (and its sibling `WebExtensionActivityEntry`) modeled on `PurgeExpiredRunLogsJobHandler`, dropping data (or at least dropping `WindowTitle`) past a retention window; confirm this doesn't already exist elsewhere in the module before implementing.
  Confidence: Low

- [Low][Quality] DesktopActivityEntry.cs:8-9 — `WindowStart` is `DateTime` while `RecordDate` is a separate `DateOnly` used as the partition key; nothing in this file enforces that `RecordDate == DateOnly.FromDateTime(WindowStart)` (or the user's local date), so a caller could set them inconsistently and misfile the row into the wrong partition.
  Why: A partition-key/date mismatch would put a row outside where date-range queries expect to find it, silently corrupting reporting queries that filter by `RecordDate`.
  Fix: Derive `RecordDate` from `WindowStart` at construction/mapping time (or add a check constraint) rather than trusting two independently-settable properties to stay in sync.
  Confidence: Low

## Notes (no severity — informational)
- Correctly derives from `BaseEntityWithUser` (portal shim), so it participates in `AppDbContext`'s global `IEntityWithUser` query filter — no per-user leak risk from missing scoping.
- Partitioning via `IsPartitionedByRange("record_date", …)` matches the documented pattern in CLAUDE.md, and the composite PK / non-concurrency-token `row_version` override is a known, intentional workaround for partitioned tables (`RETURNING xmin` limitation) — not a bug.
