# Seeder `Order` bands

`IDatabaseSeeder.Order` is a **single global sequence per seeder kind**. Seeding runs ascending;
truncation (dev seeders only) runs descending. It is how FK dependencies between seeders are
expressed, and it is the one thing a seeder cannot decide in isolation once seeders live in seven
projects. Hence bands.

**Pick a value inside your slice's band. Never reach into another slice's.** Leave gaps of 10 so a
new seeder can slot between two existing ones without a renumbering pass.

| Band | Owner | Notes |
|---|---|---|
| 000–009 | `Sydowwe.Framework` (submodule) | `UserRoleSeeder` = 4. Roles must exist before any user is created. Out of reach from this repo. |
| 010–099 | **AdhdTimeOrganizer.Core** | users, activities, roles, categories, the four activity lookups, memory anchors, timer presets |
| 100–199 | TodoLists | lists · items · steps · `TaskPriority` |
| 200–299 | Routines | routine periods · routine items · period completions |
| 300–399 | History | `ActivityHistory` |
| 400–499 | Planning | planner tasks · templates · `TaskImportance` · `Calendar` · `UserPlannerSettings` |
| 500–599 | Reminders | *(no seeders yet)* |
| 600–699 | Tracking | web-extension / desktop / android entries and their pattern mappings |
| 900–999 | host + opt-in module fixtures | reserved; see the caveat below |

## Current assignments

**App-wide default** — `UserRoleSeeder` 4 *(framework)* → `DefaultUsersSeeder` 20 *(Core)*.
The order matters: `DefaultUsersSeeder` assigns the Root role to the account it creates.

**Per-user default** — Core `DefaultActivityRole` 10, `ActivityLocationType` 20,
`ActivityWeatherDependency` 21, `ActivityExpectedCostTier` 22, `ActivityExperienceType` 23,
`TimerPreset` 30, `PomodoroTimerPreset` 31; then `TaskPriority` 100 (TodoLists),
`RoutineTimePeriod` 200 (Routines), `TaskImportance` 400, `Calendar` 410,
`UserPlannerSettings` 420 (Planning).

None of the per-user defaults FK to each other — each is a lookup owned by the user — so the
reshuffle relative to the old 1–11 sequence is safe. Note `TaskPriority` and `TaskImportance` are
**not** in the same slice: `TaskPriority` is a to-do-list concern, `TaskImportance` a planner one.

**Per-user dev** — Core lookups 10–13 → `ActivityRole` 20 → `ActivityCategory` 30 →
`Activity` 40 → the three profiles 50/51/52 → `MemoryAnchor` 60; then `TodoList` 100,
`RoutineTodoList` 200, `ActivityHistory` 300, `TaskPlannerDayTemplate` 400,
`TemplatePlannerTask` 410, `PlannerTask` 420, `WebExtensionData` 600.

The Core chain is a real FK chain (`Activity` needs its role and category; the profiles, anchors,
history rows, planner tasks and tracking entries all need the `Activity`), which is why Core holds
the whole 10–60 run and every slice sits above it. Truncation reverses that and therefore deletes
dependents first.

Two orderings changed relative to the old 5–14 sequence, both checked:
`ActivityHistorySeeder` moved from last to 300 — it reads only `Activities`, and nothing reads it;
`WebExtensionDataSeeder` moved from 12 to 600 — likewise activity-only.

## Caveat: the two module fixtures

`DevNotificationSeeder` (70) and `DevReminderSeeder` (71) are **app-wide** dev seeders living in the
`framework/` submodule, so their values could not be moved from this repo. They numerically fall in
Core's band, but they are the only two seeders in their manager's list, so nothing collides today.
Move them into 900–999 the next time the submodule is touched.
