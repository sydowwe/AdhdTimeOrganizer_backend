# Review: AdhdTimeOrganizer/infrastructure/persistence/configuration/module/NotificationsUserFkConfiguration.cs
Role: config
Summary: Three `IEntityTypeConfiguration` classes wiring cascading FKs from `Notification`, `NotificationPreference`, `PushSubscription` to the portal `User`; matches the documented erasure split exactly.
Coverage: n/a

## Issues
- [Low][Convention] NotificationsUserFkConfiguration.cs:19-53 — File name is singular (`NotificationsUserFkConfiguration.cs` per invocation vs actual class `NotificationUserFkConfiguration`) but holds three unrelated top-level configuration classes for three different entities.
  Why: Slight file/class naming mismatch could make the file harder to locate by class name; minor readability/discoverability nit, not a defect.
  Fix: Consider one file per configuration or rename the file to something like `NotificationsModuleUserFkConfigurations.cs` to signal it holds multiple classes.
  Confidence: Low

Verified: cross-checked against `DeleteUserAccountEndpoint.cs`, whose XML doc explicitly states the same three tables (`Notification`, `NotificationPreference`, `PushSubscription`) get the cascading FK here, while `notification_quiet_hours`, `reminder_kind_preference`, `reminder_recipient`, `reminder_occurrence_action`, and `reminder_dispatch.recipients_snapshot` are handled instead by `ISubjectDataEraser` implementations fanned out in that endpoint's `BeforeDeleteAsync`. No module table appears to be both FK-free here and undocumented/unhandled by an eraser — this file's scope is internally consistent with the erasure design and CLAUDE.md's description. (Did not deep-read the eraser implementations themselves to confirm they are actually registered/functional — that is out of scope for a single-file review of this config file.)
