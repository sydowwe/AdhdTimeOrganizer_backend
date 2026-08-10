# Review: AdhdTimeOrganizer/domain/model/entity/reminder/Reminder.cs
Role: entity
Summary: Clean, well-documented user-intent entity that correctly defers scheduler state to the Reminders module via IReminderRegistry rather than duplicating it; a couple of minor invariant/perf gaps.
Coverage: n/a

## Issues
- [Low][Quality] Reminder.cs:50 — `LeadOffsetsMinutes` invariants ("<= 0 and unique") are documented but enforced only by the registry service, not by the entity or a DB constraint/check.
  Why: Any other write path (seeder, migration script, future endpoint bypassing the registry) can persist a positive or duplicate offset silently.
  Fix: Add a validator on the create/update DTO and consider a CHECK constraint or a private setter + validation method on the entity.
  Confidence: Med

- [Low][Performance] Reminder.cs:47 — No index implied on `RemindAt` (and none visible on `UserId`) despite the doc stating "the day view filters this column by the user's local-day range," which is a per-user range scan.
  Why: Without a composite index (e.g. `UserId, RemindAt`) the day view query will do a full per-user scan as reminder volume grows.
  Fix: Verify/add an index in the entity configuration file (not reviewed here) covering `(UserId, RemindAt)`.
  Confidence: Low

- [Low][Quality] Reminder.cs:61 — `Recurrence` has no end condition (no end date / occurrence count) on this row; the doc says the module "walks forward from" the anchor indefinitely.
  Why: An unbounded yearly/daily recurrence with no terminal condition relies entirely on the Reminders module's occurrence calculator to avoid runaway generation or an ever-growing dispatch ledger; if that module ever materializes future occurrences eagerly instead of lazily, this is a growth/perf risk.
  Fix: Confirm (in the Reminders module review) that occurrence computation is lazy/on-demand rather than pre-materialized; if not, consider an optional `RecurrenceEndAt` here.
  Confidence: Low

- [Nit][Quality] Reminder.cs:47 — Doc says RemindAt is "(UTC)" but the CLR type is plain `DateTime`, which carries no `Kind` guarantee at the type level.
  Why: A future write path that passes a `DateTime` with `Kind=Local` or `Unspecified` would silently corrupt fire times; EF/Npgsql will store what it's given.
  Fix: Consider `DateTimeOffset` or enforce `DateTime.SpecifyKind(..., DateTimeKind.Utc)` at the write boundary (ReminderRegistrationService), and note whether that's already done there.
  Confidence: Low

No user-scoping issue found: `Reminder` derives from the portal's `BaseEntityWithUser` (closes `IEntityWithUser`), which per CLAUDE.md is covered by `AppDbContext`'s global query filter (`e.UserId == currentUserId`), so this entity does not need its own `ApplyUserScoping` override. No duplication of Reminders-module scheduler state was found — the doc comments correctly describe the split (this entity = intent, module = dispatch state via `IReminderRegistry`), and no scheduler/status/next-occurrence fields are mirrored here.
