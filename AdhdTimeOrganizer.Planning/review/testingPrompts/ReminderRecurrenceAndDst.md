# TEST-10 — reminder day-view recurrence and DST composition

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first. Use `ReminderSeedHelper` — a reminder test that
writes only the portal row tests half the seam.

## Why

The reminder surface is the best-covered part of Planning (16 tests in `ReminderRegistrationTests`,
plus the auth matrix), but two pieces of real calendar arithmetic are untouched:

1. `GetByDateReminderEndpoint.OccursOn` — whether a recurrence anchored at instant X lands on local date
   D. Five cadences, month-end and leap-year semantics.
2. The two places wall-clock is converted to an instant across a DST boundary:
   `GetByDateReminderEndpoint.ToUtc:114-121` and
   `ReminderRegistrationService.ComposeTaskInstant:263-286`.

The three existing by-date tests (`ReminderCrudAuthMatrixTests.ReminderByDateAuthTests`) cover 401,
plain-User access and cross-user exclusion — none of the maths.

## Where

`AdhdTimeOrganizer.IntegrationTests/Reminders/` — a new `ReminderDayViewTests`.

## Part 1 — `OccursOn`

`GET /api/reminder/by-date/{date}` where `{date}` is the caller's **local** date (`yyyy-MM-dd`).

The endpoint splits into two queries: one-shots filtered in SQL by the local-day instant range, and
recurring ones filtered to `RemindAt < dayEnd` and then expanded **in memory**. The response carries
both `RemindAt` (the stored anchor) and `OccursAt` (the instant it fires on the requested day) — assert
both; a naive implementation returns the anchor for `OccursAt` and the test still looks plausible.

Give the test user a non-UTC `User.Timezone` (`Europe/Bratislava`) so the local-day boundary is doing
work.

| Recurrence | Anchor | Query date | Expect |
|---|---|---|---|
| `Daily` | any past date | any later date | present |
| `Daily` | tomorrow | today | **absent** — `date < anchorDate` returns false |
| `Weekly` | a Tuesday | the next Tuesday | present |
| `Weekly` | a Tuesday | the next Wednesday | absent |
| `Monthly` | the 15th | the 15th of a later month | present |
| `Monthly` | the **31st** | a 30-day month | **absent** — documented `AddMonths` semantics, not a bug |
| `Monthly` | the 31st | the next 31-day month | present |
| `Quarterly` | 15 Jan | 15 Apr | present (`MonthsApart % 3 == 0`) |
| `Quarterly` | 15 Jan | 15 Mar | absent |
| `Quarterly` | 15 Jan | 15 Jan **next year** | present — 12 months apart; guards the `(to.Year - from.Year) * 12` term |
| `Yearly` | **29 Feb** 2028 | 29 Feb 2032 | present |
| `Yearly` | 29 Feb 2028 | 28 Feb 2029 | **absent** — 29 Feb recurs on 29 Feb only |
| one-shot | inside the local day | that day | present, `OccursAt == RemindAt` |
| one-shot | 23:30 local, i.e. the *previous* UTC day | that local day | present — this is the case a UTC day boundary gets wrong |

Also: a recurring reminder anchored **in the past** must appear (that is the birthday case
`ReminderRegistrationTests.PastAnchoredRecurringReminder_IsAccepted` allows in), and results are ordered
by `OccursAt` ascending across both kinds.

## Part 2 — DST

Two distinct code paths; test both.

### A — `ComposeTaskInstant` spring-forward gap

`ReminderRegistrationService.cs:269-276`. In `Europe/Bratislava`, 02:30 on the last Sunday of March does
not exist. Create a planner task at `StartTime = 02:30` on that date, attach a reminder, and assert:

- The write **succeeds** — the comment is explicit that a DST gap must not make a task unsaveable.
- `Reminder.RemindAt` is the first instant that does exist (the code adds the adjustment rule's
  `DaylightDelta`, falling back to one hour).

### B — `ComposeTaskInstant` fall-back ambiguity

Last Sunday of October, 02:30 exists twice. Assert the reminder resolves to the **earlier**
(standard-time) occurrence — that is what `TimeZoneInfo.ConvertTimeToUtc` does silently, and the code
logs it rather than choosing. Pin the choice so a future `TimeZoneInfoOptions` change is visible.

### C — `GetByDateReminderEndpoint.ToUtc` gap handling

`:114-121` nudges an invalid local time forward by a **hard-coded hour**, unlike `ComposeTaskInstant`
which reads the adjustment rule. Request the day-view for the spring-forward date and assert it returns
200 with the day's reminders rather than throwing — and note in a comment that the two paths handle the
gap differently, which is worth reconciling.

## Part 3 — one gap worth pinning while you are here

`ReminderRegistrationService.SyncCoreAsync:89-95` calls `SaveChangesAsync` **inside** the per-reminder
loop of `SyncForPlannerTasksAsync` (`PERF-3`), after the method deliberately batch-loaded tasks and
timezones to avoid N+1. Seed one planner task with three attached reminders, move its start time via
`PATCH /api/planner-task/{id}`, and assert all three reminders end up at the new derived instant. The
test is about correctness; it also gives the eventual single-save refactor a green pin.
