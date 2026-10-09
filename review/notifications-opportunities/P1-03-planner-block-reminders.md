# P1-03 — Planner block reminders (audit N1), the materialiser sweep, and Google's double alert

> Paste into a fresh session. Read `review/notifications-opportunities/_SHARED-CONTEXT.md` first.

## Facts (verified 2026-10-09)

- A `Reminder` row exists only if the user adds one per task. `UserPlannerSettings.RemindersEnabled` (default
  `true`) and `ReminderMinutesBefore` (default `10`) only **prefill the form** — Planning `domain-map.md` §
  *Reminders*: "`RemindersEnabled = false` suppresses the prefill, never the reminder"; the server side is
  `ReminderRegistrationService.ApplyUserDefaultsAsync`.
- `AdhdTimeOrganizer.Planning/application/service/taskPlanner/RepeatingTaskMaterialiser.cs:40-41`: *"No planner-task
  create registers one today (audit N1 is unbuilt) … When N1 lands, this is a create path it has to cover."*
- Lifecycle after creation is already handled: 16 sites call `SyncForPlannerTasksAsync` / `CancelManyAsync` (moves,
  status patches, deletes, template applies, re-plan, the auto-complete job, welcome-back). `SyncCoreAsync` retires a
  reminder when the task is `Completed` or `Cancelled` (a skip is `Cancelled` + reason).

**Creation paths N1 must cover:**

| Path | Note |
|---|---|
| `CreatePlannerTaskEndpoint` | plain create |
| `PlannerTemplateApplier` (`:117`, `:194`) | behind `ApplyTemplatePlannerTaskEndpoint`, `ApplyTemplateBatchPlannerTaskEndpoint` **and** FO6's `RepeatingTaskMaterialiser` (via `StageAgainstDay`) |
| `CopyToDaysPlannerTaskEndpoint:106` | deliberately carries no execution state; the mode decides, not the source |
| `CarryOverPlannerTaskEndpoint` | **check** whether it creates a task or moves one (it already re-syncs) |
| host `ApplyFreshStartWelcomeBackEndpoint` | carries tasks forward |

## Design

### 1. A mode, not a bool

A full day is 10–15 blocks; a push for each is the noise that makes users mute everything. Replace "prefill on/off"
with **`AutoReminderMode { Off, ImportantOnly, All }`** on `UserPlannerSettings` (migration; keep `RemindersEnabled`
for the prefill or fold it in — decision).

- Eligible: foreground (not background — reuse the notion `SyncCalendarToGoogleEndpoint.IsPushable` uses), not
  optional (`TaskImportance.OptionalMarkerValue` 666) unless `All`, `NotStarted`, start in the future.
- `ImportantOnly` — **decision**: Critical only (`CriticalMarkerValue` 999) or a threshold on the user's importance scale.
- Lead: `LeadOffsetsMinutes = [-ReminderMinutesBefore]`.

### 2. Auto reminders are distinguishable

Add `Reminder.IsAuto` (migration). Rules:

- Created **at task creation only**. Re-syncs never re-create one, so a user who deletes an auto reminder wins.
- A settings change applies to tasks created afterwards (optionally a one-off "apply to today and tomorrow").

### 3. Its own kind (recommended)

Register auto reminders under a new `NotificationType.PlannedBlockStarting` rather than `PersonalReminder`:

- users get a separate settings row — they can mute the automatic ones without muting the ones they wrote;
- it can go in `HeldWhileAwayTypes` (system-generated and about a moment), while `PersonalReminder` stays
  un-held because the user asked for it.

That means `ReminderRegistrationService.RegisterAsync` picks the type from `IsAuto` (today it hard-codes
`PersonalReminder` at `:120`), and a second `Kind` in `PersonalReminderKindCatalog` so it can be muted in
`/reminder-preference` too.

## The materialiser sweep (FO6 × N1)

`RepeatingTaskMaterialiser` runs only from `GET /calendar/day-plan/{date}`, `GET /calendar/by-Date/{date}` (today and
tomorrow) and after a rule's create/update. A user who doesn't open the app that day gets **no materialised block —
and therefore no reminder**, which is exactly the day they needed one.

Add a scheduled sweep (Planning registrar): every 15 min, zone-aware, once per user per local day (e.g. after local
midnight, or before `WakeUpTime`), materialise today + tomorrow for users with active `AutoPlace` rules; the creation
path then creates the auto reminders.

⚠ **The trap:** the materialiser has only ever run inside an authenticated request. In a job,
`BaseWithUserEntitySaveChangesAsync` cannot fill `UserId`, and an insert dies with `UserId == 0`. Verify how it sets
`UserId` and test the job path explicitly. It also commits and never fails its caller — run each user in its own scope
and clear the tracker on failure, as the summary describes. Skip users who are away (`UserPresence.QuietAfter`).

## Google Calendar would double every alert

`AdhdTimeOrganizer/application/endpoint/calendar/SyncCalendarToGoogleEndpoint.cs:97` builds `new Event { … }` without
`Reminders`, so Google applies the calendar's default popups to every synced block. With N1, a phone with Google
Calendar rings twice per block.

Recommended: for tasks that have a portal reminder, set `Reminders = new Event.RemindersData { UseDefault = false,
Overrides = [] }` — the portal's reminder respects quiet hours, the away hold and snooze; Google's does not. Tasks
without a portal reminder keep Google's defaults. (Sync is manual and per day today.)

## Tests (rows, not routes)

- Each creation path × each mode: `Reminder` rows (portal) **and** `ReminderDefinition` rows (module).
- A deleted auto reminder is not re-created by a later move / status patch.
- The materialiser job, run unauthenticated, inserts the occurrence with the right `UserId` and its reminder.
- `SyncCalendarToGoogleTests`: a task with a portal reminder is pushed with `UseDefault = false`.
- `PlannedBlockStarting` for an away user → bell row, no push (`NotificationAwayHoldTests` pattern).

## Open decisions

1. Mode values and what "important" means.
2. New kind (recommended) vs reusing `PersonalReminder`.
3. Whether a settings change back-fills today/tomorrow.
4. Google: override reminders (recommended) vs skipping N1 for synced tasks.
