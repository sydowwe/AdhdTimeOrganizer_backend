# Shared context for every prompt in this folder

Read this before any `P*.md` here. `CLAUDE.md` auto-loads and covers solution-wide conventions; this file
carries only what the notification / reminder / scheduler work needs. Framework paths were read at
**5646e89** — check that `framework/` is checked out there (`git -C framework log -1`), see
[`README.md`](README.md#framework-revision). The `P3-0*` prompts were read later, at framework **423569f**
(2026-10-13). The frontend repo is `C:\Users\jakub\WebstormProjects\AntiProcrastinationApp-frontend`.

## Build and test

Always `./scripts/test.ps1` (see `CLAUDE.md`). Docker must be up. Integration tests live in
`AdhdTimeOrganizer.IntegrationTests`; there are no per-slice test projects.

## The three modules, in one paragraph each

- **Notifications** (`framework/Sydowwe.Notifications`) — *how/where*. `INotificationService.NotifyAsync(recipients,
  payload, ct)`; the type is derived from the payload record's attribute. Writes a bell row, pushes SignalR (InApp),
  Web Push and Email per recipient preferences. Quiet hours **defer** push/email (`DeferredUntil`, flushed every
  15 min); a kind declaring `HeldWhileAway`, for a recipient `IRecipientEngagementReader` reports away (FO5: 3+ days,
  `UserPresence.QuietAfter`) gets the bell row only, and **nothing releases it later**. Dismiss is a hard delete.
- **Reminders** (`framework/Sydowwe.Reminders`) — *when, on a schedule*. Owners register through
  `IReminderRegistry` (Contracts); a scan finds due occurrences and calls `NotifyAsync`. Supports one-shot with
  lead offsets, recurring interval, cron; per-recipient snooze / dismiss / undo; per-kind opt-out; digests. Its
  summary (`docs/summary.md`, ~730 lines) is the single oracle — read the relevant section, not all of it.
- **Scheduler** (`framework/Sydowwe.Scheduler`) — *the substrate*. Keyed `IScheduledJobHandler`s, a registry, a
  run log, failure and overdue alerts. Nobody but `Sydowwe.Scheduler` touches Quartz.

## Adding a `NotificationType` — the full checklist

Since R1 a kind belongs to the slice that raises it, so **adding one touches no framework file** and is a
single-repo change. Worked example: `AdhdTimeOrganizer.TodoLists/application/notification/TodoListsNotification.cs`
(key, payload and kind for `TodoItemDue`, one file).

1. **In the owning slice's `application/notification/`** (the host's own kinds sit beside their producer, e.g.
   `application/service/morningBriefing/`):
    - the key — a `const string …Key` and a `static readonly NotificationType` built from it, in the slice's
      `…NotificationTypes` class. **Only add, never rename**: the key is stored and on the wire.
    - the payload record, tagged `[NotificationPayload(…NotificationTypes.XKey)]`, implementing
      `INotificationPayload`. **Ids and non-person scalars only**; `PayloadPiiContractGuardTests` fails the build on
      person-data names. A user-authored title of the user's *own* thing is allowed (`PersonalReminderPayload.Title`,
      `TimerBoundaryPayload`'s activity name are the precedents). Property names are the persisted JSON keys.
    - the kind — `sealed class XNotificationKind : NotificationKind<XPayload>, ISingletonService`. `Render` words
      it in SK and EN (switch on `context.Kit.Language`, Slovak as the default arm) with a **name-less fallback**
      for a `null` payload; `Subject` / `PushMeta` if it links somewhere. Override only the policy that differs
      from the defaults, with the reason in its doc: `HeldWhileAway` (unsolicited and about a moment),
      `PushOnByDefault => false` (P2-02: a bell-only kind — push off until the user ticks it), `EmailOnByDefault`
      (almost never here), `QuietHoursExempt` (only when the user picked the instant moments ago), `Actions`
      (P2-03: at most two; every one but Dismiss needs an `INotificationActionHandler` in the owning slice;
      Snooze shows only on a push the Reminders scan raised).
2. **The host's second copies**, in `AdhdTimeOrganizer.IntegrationTests/Endpoints/`: the key in
   `NotificationCatalogTests.Catalog` (and its buttons in `.Actions`), and a row in
   `NotificationKindPolicyTests.Expected`. Nothing in `ModuleServiceExtensions` — registering the kind puts it in
   the catalog. If the kind never registers (its slice missing from `ModuleAssemblies`) it dispatches with the
   generic text and no settings row; these tests and `NotificationVocabularyTests` are what turn that red.
3. Frontend (`AntiProcrastinationApp-frontend`): `src/app/notifications/notificationTypeMeta.ts` (icon, deep link).

## Producing a notification

- One producer service per slice knows the payload shapes (`IRoutinePeriodNotificationService` is the model:
  `AdhdTimeOrganizer.Routines/application/service/RoutinePeriodNotificationService.cs`). It calls `NotifyAsync`
  **after** the business commit, catches, and logs **ids only** — titles are user-authored and must not reach logs.
- Every producer must be **one-shot per occurrence** (mark what you notified, compare marks against a freshly
  computed instant like `EndingSoonNotifiedFor` does, and mark only when a notification actually went out).

## Registering a reminder (the Reminders module)

Worked example: `AdhdTimeOrganizer.Planning/application/service/ReminderRegistrationService.cs`.

- `ReminderKey(OwnerModule, SubjectType, SubjectId, Kind)` is the idempotency identity; `RegisterAsync` upserts.
- The owner **re-registers on every change** and **cancels on delete**. A DB cascade never reaches the module —
  read the ids *before* the delete, cancel *after* the commit (`ApplyTemplatePlannerTaskEndpoint` is the example).
- **Always register after your own row is saved** — the registry writes through the same scoped `DbContext` and
  commits.
- A payload is **frozen at registration**. Anything that must be live at fire time is either a sweep (see
  `RoutinePeriodNudgeJobHandler`'s doc comment for that reasoning) or resolved by an owner seam at dispatch.
- **Self-cancelling reminders:** register with `RecipientMode.ResolverStrategy` and implement
  `IReminderRecipientResolver` (`Sydowwe.Framework.Contracts/reminders/IReminderRecipientResolver.cs`, keyed,
  runs unauthenticated, may read the DB). Returning **no recipients** makes the scan write a `Skipped /
  NoRecipients` dispatch row and send nothing. That is a safety net for a forgotten cancel path, which in this
  solution fails silently.
- Let users mute a kind before it exists: `IReminderKindCatalog` (example
  `AdhdTimeOrganizer.Planning/application/service/PersonalReminderKindCatalog.cs`). Label rows on `my-upcoming`:
  `IReminderLabelResolver` (example `PersonalReminderLabelResolver.cs`) — **pure, synchronous, never throws**.

## Adding a scheduled job

- `IScheduledJobHandler` + `IScopedService` with a `HandlerKey` const; inject what you need (the dispatcher opens
  a scope per fire). Register in the owner's `*ScheduledJobsRegistrar` (hosted service, **required on every boot**
  — the job store is in-memory). Add the key to `ModuleWiringTests`' job-key assertions.
- `ScheduleSpec.FromCron(cron)` with no zone runs in **UTC** (`SchedulerService.ResolveTimeZone`, framework
  `Sydowwe.Scheduler/infrastructure/SchedulerService.cs:300`). For "at the user's local time", copy
  `AdhdTimeOrganizer.Routines/infrastructure/jobs/RoutineSuggestedTodayJobHandler.cs`: 15-min cron, zones via
  `IUserTimeZoneReader.GetTimeZonesAsync`, the day's `WakeUpTime` via Core's `ICalendarDayLookup`, a per-user
  per-local-date mark written right after a successful send.
- **Background jobs run unauthenticated**: the global user filter is inert (you see every user — usually the point),
  and inserting an `IEntityWithUser` row needs an **explicit `UserId`** or it dies on the FK with `UserId == 0`.
- Inject `TimeProvider` so tests can fix the clock (`CompleteFinishedPlannerTasksJobHandler` does).

## The user's day and presence

- Zone: `IUserTimeZoneReader` (bulk) / `User.Timezone`. Wake-up and bed time per day:
  `ICalendarDayLookup.GetDaysAsync` → `CalendarDaySummary.WakeUpTime` / `BedTime`. Bed time defaults to 00:00
  ("end of day") until FO10.
- Presence: `User.LastActiveAt` (stamped hourly by `LastActiveStampMiddleware`; tracker tokens never stamp),
  thresholds in Core's `UserPresence`. Welcome-back card: `User.WelcomeBackAwayFrom` / `WelcomeBackReturnedAt`.

## Tests — assert on rows, not routes

The silent-failure rule from `CLAUDE.md` applies to every item here. Models to copy:
`NotificationAwayHoldTests` (what the fake senders received, live and after the flush),
`RoutineAwayExcusalFlowTests`, `ReminderKindCatalogTests`, `ReminderDisplayLabelTests`,
`TimerBoundaryAlarmRegistrarTests`, `DayReplanTests` (fixed `TimeProvider`), `ModuleWiringTests`.

## PII

Payloads carry ids. Logs carry ids. Never a name, email address or user-authored title in a log line.
