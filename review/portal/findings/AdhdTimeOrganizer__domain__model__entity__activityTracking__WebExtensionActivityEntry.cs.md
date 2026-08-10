# Review: AdhdTimeOrganizer/domain/model/entity/activityTracking/WebExtensionActivityEntry.cs
Role: entity
Summary: Well-formed partitioned per-user ledger entity, but stores browsing URLs/domains with no retention purge and no audit-exclusion for that PII once auditing is wired up.
Coverage: n/a

## Issues
- [High][Compliance] WebExtensionActivityEntry.cs:9-10 — `Domain` and `Url` capture the user's browsing history (full URL up to 2048 chars) with no retention purge job found anywhere in the codebase (checked for `Purge*WebExtension*`/`WebExtension*Retention*` — none exist), unlike the Scheduler/Reminders/Notifications ledgers CLAUDE.md documents as having a purge handler.
  Why: This is exactly the append-only-ledger-with-PII case CLAUDE.md's "Ledger Retention" section calls out (GDPR Art. 5(1)(e) / §13 zák. 18/2018) — browsing history should not be retained indefinitely, and the query filter (`RecordDate >= CurrentPartitionDate`, i.e. now-2y) only hides old rows from EF reads, it does not delete them; raw SQL/reporting against the partitions still sees years of URL history.
  Fix: Add a `RetentionOptions`-derived policy + purge job for this table (delete/truncate partitions older than the retention window), following the pattern in `PurgeExpiredRunLogsJobHandler`/`PurgeExpiredReminderLedgersJobHandler`.
  Confidence: Med

- [Medium][Security] WebExtensionActivityEntry.cs:10 — `Url` (and to a lesser extent `Domain`) is sensitive browsing PII with no `[AuditIgnore]`/`[NoAudit]` attribute.
  Why: Auditing is currently unwired per CLAUDE.md, so there's no active leak today, but if/when `AuditSaveChangesInterceptor` is turned on, full URLs would be captured verbatim into `ChangedProperties` snapshots in `audit_log`, duplicating the sensitive data into a second, longer-lived, less-access-controlled store.
  Fix: Pre-emptively mark `Url` (and consider `Domain`) with `[AuditIgnore]` so audit wiring doesn't silently start persisting browsing history a second time.
  Confidence: Low

- [Low][Quality] WebExtensionActivityEntry.cs:10 — `Url` is declared `required string?` — required-but-nullable is a contradictory signal (caller must supply the property but may supply null).
  Why: Slightly misleading API; a reader can't tell from the declaration whether "no URL" is a valid, expected state or an omission.
  Fix: If URL-less entries are legitimate (e.g. domain-only tracking), document it with a comment; otherwise drop the nullability.
  Confidence: Low

- [Nit][Quality] WebExtensionActivityEntry.cs:8 — `WindowStart` comment "Always 1-min aligned" is an unenforced invariant (no validator/check constraint visible in the configuration file).
  Why: Nothing prevents a caller from writing an unaligned timestamp; downstream aggregation (stacked bars/timeline endpoints) presumably relies on the alignment.
  Fix: Enforce via a DB check constraint or validation in the write path, or note where it's enforced if it already is elsewhere.
  Confidence: Low
