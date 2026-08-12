# Seeder `Order` bands

`IDatabaseSeeder.Order` is a **single global sequence per seeder kind**. Seeding runs ascending;
truncation (dev seeders only) runs descending. It is how FK dependencies between seeders are
expressed, and it is the one thing a seeder cannot decide in isolation once seeders live in seven
projects. Hence bands.

**Pick a value inside your slice's band. Never reach into another slice's.** Leave gaps of 10 so a
new seeder can slot between two existing ones without a renumbering pass.

⚠ **One band is shared by two projects, and it has to be.** `AdhdTimeOrganizer.ActivityProfiles` was
extracted out of Core but its seeders sit *inside* Core's 010–099 run rather than above it, because
the dev chain genuinely interleaves: the four activity lookups (10–13) must exist before Core's
`Activity` (40), and the three profiles (50–52) and `MemoryAnchor` (60) must come after it. Giving
the slice its own contiguous band above Core would put the lookups after the profiles that FK into
them and truncation would try to delete a lookup while a profile still referenced it. So this one
band is ordered by the FK chain, not by project. It is the only shared band; every other slice sits
wholly above Core.

| Band | Owner | Notes |
|---|---|---|
| 000–009 | `Sydowwe.Framework` (submodule) | `UserRoleSeeder` = 4. Roles must exist before any user is created. Out of reach from this repo. |
| 010–099 | **AdhdTimeOrganizer.Core** *and* **AdhdTimeOrganizer.ActivityProfiles** | users, activities, roles, categories, timer presets (Core); the four activity lookups, the three profiles, memory anchors (ActivityProfiles) |
| 100–199 | TodoLists | lists · items · steps · `TaskPriority` |
| 200–299 | Routines | routine periods · routine items · period completions |
| 300–399 | History | `ActivityHistory` |
| 400–499 | Planning | planner tasks · templates · `TaskImportance` · `Calendar` · `UserPlannerSettings` · **reminders** |
| 500–599 | *(retired)* | Was Reminders. Reminders folded into Planning on 2026-08-11 — the `Reminder` ↔ `PlannerTask` coupling is bidirectional. A reminder seeder, if one ever appears, belongs in 400–499. |
| 600–699 | Tracking | web-extension / desktop / android entries and their pattern mappings |
| 900–999 | host + opt-in module fixtures | reserved; see the caveat below |

## Current assignments

**App-wide default** — `UserRoleSeeder` 4 *(framework)* → `DefaultUsersSeeder` 20 *(Core)*.
The order matters: `DefaultUsersSeeder` assigns the Root role to the account it creates.

**Per-user default** — Core `DefaultActivityRole` 10; ActivityProfiles `ActivityLocationType` 20,
`ActivityWeatherDependency` 21, `ActivityExpectedCostTier` 22, `ActivityExperienceType` 23; Core
`TimerPreset` 30, `PomodoroTimerPreset` 31; then `TaskPriority` 100 (TodoLists),
`RoutineTimePeriod` 200 (Routines), `TaskImportance` 400, `Calendar` 410,
`UserPlannerSettings` 420 (Planning).

None of the per-user defaults FK to each other — each is a lookup owned by the user — so the
reshuffle relative to the old 1–11 sequence is safe. Note `TaskPriority` and `TaskImportance` are
**not** in the same slice: `TaskPriority` is a to-do-list concern, `TaskImportance` a planner one.

**Per-user dev** — ActivityProfiles lookups 10–13 → Core `ActivityRole` 20 → `ActivityCategory` 30 →
`Activity` 40 → ActivityProfiles' three profiles 50/51/52 → `MemoryAnchor` 60; then `TodoList` 100,
`RoutineTodoList` 200, `ActivityHistory` 300, `TaskPlannerDayTemplate` 400,
`TemplatePlannerTask` 410, `PlannerTask` 420, `WebExtensionData` 600.

The 10–60 run is a real FK chain (`Activity` needs its role and category; the profiles, anchors,
history rows, planner tasks and tracking entries all need the `Activity`), which is why it is held
jointly by Core and ActivityProfiles and every *other* slice sits above it. Truncation reverses that
and therefore deletes dependents first — which is exactly why the lookups keep 10–13 rather than
moving above the profiles that FK into them.

Two orderings changed relative to the old 5–14 sequence, both checked:
`ActivityHistorySeeder` moved from last to 300 — it reads only `Activities`, and nothing reads it;
`WebExtensionDataSeeder` moved from 12 to 600 — likewise activity-only.

## Caveat: the two module fixtures

`DevNotificationSeeder` (70) and `DevReminderSeeder` (71) are **app-wide** dev seeders living in the
`framework/` submodule, so their values could not be moved from this repo. They numerically fall in
Core's band, but they are the only two seeders in their manager's list, so nothing collides today.
Move them into 900–999 the next time the submodule is touched.
