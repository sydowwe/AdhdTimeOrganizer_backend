# TEST-3 — Tests for the activity-planning endpoints

## Context you need (do not rediscover)

**Repo:** `C:/Users/jakub/RiderProjects/AdhdTimeOrganizer`

**Endpoints:** `AdhdTimeOrganizer/application/endpoint/activityPlanning/` — subfolders
`calendar/`, `plannerTask/`, `repeatingPlannerTask/`, `taskPlannerDayTemplate/`,
`templatePlannerTask/`, `taskImportance/`, `plannerSettings/`.

**Entities:** `domain/model/entity/activityPlanning/` — `BasePlannerTask` and its three concrete
types (`PlannerTask`, `RepeatingPlannerTask`, `TemplatePlannerTask`), `TaskImportance`,
`TaskPlannerDayTemplate`, `UserPlannerSettings`; plus `domain/model/entity/Calendar.cs`.

**Test project:** `AdhdTimeOrganizer.IntegrationTests`, new files under `Endpoints/`. Read
`Infrastructure/AppDbContextFixture.cs`, `Infrastructure/AuthTestBase.cs`, and
`Reminders/ReminderSeedHelper.cs` (planner tasks and reminders are entangled — see D).
xunit v3, FluentAssertions, `[Collection("Postgres")]`. Clients via `CreateUserRoleClient()` and
`CreateFactory(roles, userId)` for a second user. `CreateDbContext()` + `SeedAsync(db)`.

**Discover request/response shapes yourself** — DTOs under `application/dto/request/taskPlanner/`
and `application/dto/response/taskPlanner/`; routes from each endpoint's `Configure()` or its
framework base. Note portal DTOs use **`TimeDto`** for time-of-day, **not** `TimeOnly` and **not**
`MyIntTime` (that one is module-side only). Call `.ToTimeOnly()` when comparing against entities.

Prefer subclassing the framework test bases (`framework/Sydowwe.Framework.Testing/baseTests/`) for
the plain CRUD sets — they ship the auth matrix and 404 paths. **No portal endpoint currently
subclasses them**, so you will be first; report friction rather than silently hand-rolling.

## Domain rules (`docs/domain-map.md` → Day planning)

- A day is a `Calendar` row, **one per user per date** (`Calendar(UserId, Date)` unique). Days exist
  only once planned — which is why `Reminder` anchors on an absolute instant rather than a `CalendarId`.
- `PlannerTask.IsDone` is **derived** (`Status == Completed`); `Status` is the stored truth.
- `IsOptional` is `Importance.Importance == 666` — a sentinel rank, not a flag (`CQ-15`).
- Planner tasks **may overlap**; overlap is a resolution strategy at template-apply time, not a rule.
- Delete behavior: `Calendar` → `PlannerTask` **Cascade**; `PlannerTask` → `Reminder` **Cascade**;
  `Activity` → `PlannerTask` **Restrict**; `TaskImportance` → tasks **SetNull**;
  `TodoListItem` → `PlannerTask.TodolistItemId` **SetNull**.
- `TaskImportance(UserId, Importance)` is unique — reordering is a **swap**, not an insert.

## Scenarios to write

### A. IDOR and auth matrix (write first)

For every `{id}` route across all seven subfolders: seed two users, and as user B attempt read,
update, delete and patch against user A's ids. Assert 404/403 and assert from a fresh context that
user A's row is unchanged. Then the auth matrix: unauthenticated → 401; `User` role → allowed
(CLAUDE.md: the default is User + Admin + Root because every account here is a plain `User` — flag
any planning route demanding admin as a probable bug); extension-client token → denied by the
`DenyExtensionClients` policy.

`GetByDateCalendarEndpoint` is the SPA's main read — give it explicit IDOR coverage by date, not
just by id.

### B. `ApplyTemplatePlannerTaskEndpoint` — the four conflict-resolution modes

This is the most intricate endpoint in the portal. Per the domain map it stamps the template onto the
calendar (name, id, default wake/bed times), bumps `UsageCount` / `LastUsedAt`, and resolves overlaps
by `ApplyTemplateConflictResolutionEnum`:

- **`Ignore`** — drop conflicting *new* tasks.
- **`Overwrite`** — delete conflicting *existing* ones.
- **`MergeIgnore`** — carve the *new* tasks around existing ones.
- **`MergeOverwrite`** — carve the *existing* tasks around new ones.

Carving **splits a task around each blocker and can produce several segments from one task**. Write
one test per mode with a deliberately awkward geometry: an existing task fully containing a new one,
a new one fully containing an existing one, partial overlap at each end, and one blocker in the
middle of a long task (which must yield **two** segments).

Assert on the resulting task set (start/end pairs), `UsageCount`/`LastUsedAt`, and the stamped
calendar fields.

**Reminder interaction:** reminders orphaned by any deletion are collected from the ChangeTracker
**before** the save and cancelled after it. Seed reminders on tasks that `Overwrite`/`MergeOverwrite`
will delete, and assert those registrations are cancelled — this is the ordering `CQ-11` warns has no
compensation if the cancel throws.

### C. `PatchPlannerTaskStatusEndpoint` and `PatchPlannerTaskSpanEndpoint`

Status is the driver of the whole completion fan-out, so:

- Setting `Completed` sets `IsDone` derived-true and syncs the linked `TodoListItem` / matching
  `RoutineTodoList`. (Deep coverage lives in `CompletionFanOutEventHandlers.md` — here just assert
  the endpoint's own contract and that it returns 2xx.)
- Setting `Cancelled` or `NotStarted` **clears `ActualStartTime`/`ActualEndTime`**. This endpoint
  does it correctly; `TodoListItemIsDoneChangedEventHandler` does not (`CQ-7`), so pin the correct
  behavior here as the reference.
- Status change **retires the task's reminder** (`SyncForPlannerTasksAsync`). Assert it.
- Span patch: drag-resize must not silently produce `EndTime < StartTime`. Related, `CQ-14`:
  `BasePlannerTask.IsNextDay` is commented out and `TimeOnly` cannot express an overnight task —
  try creating a 23:00→01:00 task and record what happens. If it is accepted and produces a negative
  duration, that is a finding, not a test to bend.

### D. `GetSuggestionsRepeatingPlannerTaskEndpoint` — three tiers, de-duplicated in order

1. `RepeatingPlannerTask` the user set (`SourceType = UserSet`), matched by `RecurrenceType`:
   day-of-week, day-of-month, active date range, or day type.
2. Planner patterns from `mv_planner_task_pattern` (`PlannedPattern`) — **skipped** for activities
   already covered by tier 1.
3. History patterns from `mv_activity_history_pattern` (`HistoryPattern`) — **skipped** for anything
   covered by tier 1, or by the same (activity, pattern type, value) in tier 2.

A pattern exists only at **≥ 3 occurrences** of the same (activity, ISO day-of-week *or*
day-of-month), excluding cancelled tasks (`status != 4`), and reports average start/end times.
`pattern_type` values are the `RecurrenceType` enum's ints (0 = DayOfWeek, 1 = DayOfMonth).

Tests: exactly 2 occurrences → **no** pattern; exactly 3 → pattern appears; 3 including a cancelled
one → **no** pattern; a tier-1 entry suppresses the tier-2 suggestion for the same activity; a tier-2
entry suppresses the matching tier-3 one; average start/end are actually averaged.

⚠ These views are materialized and refreshed by `SuggestionPatternRefreshInterceptor` on save. After
seeding, make sure the refresh has happened (save through `AppDbContext` rather than raw SQL) or the
suggestions will be empty and the tests will look green for the wrong reason. **Assert the view is
non-empty before asserting on suggestion content** — otherwise this whole section is assertion theater.

### E. `UserPlannerSettings` — one per user

Assert a user cannot end up with two settings rows, and that a missing row yields sane defaults
rather than a 500. `UserPlannerSettings.ReminderMinutesBefore` supplies `[-N]` when a task-linked
reminder request omits lead offsets — assert that fallback.

### F. `TaskImportance` reorder is a swap

`(UserId, Importance)` is unique, so reordering must swap rather than insert. Assert a reorder does
not transiently violate the index (a naive implementation throws 23505), and that the `666` optional
sentinel is preserved.

### G. Delete behavior

Deleting a `Calendar` cascades its tasks; deleting an `Activity` still referenced by a task is
**Restricted** (clean 409, not 500); deleting a `TaskImportance` SetNulls the reference rather than
deleting tasks.

### H. `SyncCalendarToGoogleEndpoint`

Requires a connected Google account. Assert the not-connected path returns a clean, distinguishable
error rather than a 500 — `CQ-20`/`GoogleCalendarService` has no try/catch around Google API calls,
so a revoked token currently surfaces as an unhandled exception. Stub the Google service; do not
call the real API from tests.

## Conventions

- AAA; fresh `CreateDbContext()` for post-request assertions.
- Two distinct seeded users throughout.
- Never assert on or log emails/names — ids only.
- If an IDOR test in **A** fails, that is a 🔴 finding: stop and report, do not weaken it.
