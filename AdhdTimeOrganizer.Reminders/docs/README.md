# Reminders

> Schedules user-facing reminders and deadlines (probation ending, document expiry, …) and dispatches
> them on time through the Notification module.

## What it does

Owns *when a notification fires on a schedule*. Owning modules register reminders/deadlines as data through a Kernel contract; this module runs a single recurring scan that finds due occurrences and
hands the actual send to the Notification module. It owns neither the transport (Notifications) nor the Quartz substrate (Scheduler) — it sits on top of both.

## Setup / running

No module-specific setup beyond the standard EF migrations. The module is complete: the `Kernel.reminders`
contract, the persistence model, the registry + command/inspector endpoints, the recurring scan (registered with the Scheduler module), the dispatch policy (per-kind opt-out, quiet hours, opt-in
digests), the dashboard reads/exports, and per-recipient snooze/dismiss all ship here. The schema ships across the
`RemindersModule`, `RemindersDispatchPolicy` and `RemindersSnoozeDismiss` migrations in each deployment portal (`…AdminPortal.Sandbox`, `…HBCleaning.AdminPortal`) — apply them with the usual
`dotnet ef database update`
for that portal. Entities + EF config: `domain/entity/` + `infrastructure/persistence/configuration/`.

## Docs

- `summary.md` — start here if you're working in this module (the three-module split + the Kernel contract surface).
