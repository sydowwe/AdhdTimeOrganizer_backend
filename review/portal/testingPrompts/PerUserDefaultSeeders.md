# TEST-18 — Per-user default seeders (seeder-level, not matcher-level)

## Context

`AdhdTimeOrganizer.Core/.../seeder/SeederOrderBands.md` documents the seeder taxonomy — read it first;
per `CLAUDE.md` seeders are picked by owner (app-wide / per-user) × purpose (production / dev fixture),
and only dev seeders truncate. `Seeding/PerUserDefaultMatcherTests.cs` already pins
`PerUserDefaultMatcher` itself (the shared matching logic, count-based guards, positional-reset bugs —
per `CLAUDE.md` these guards exist specifically to keep two previously-shipped `23505` (unique
violation) bugs dead). **That test does not exercise any individual seeder** — it's a pure unit test of
the matcher in isolation.

The concrete per-user default seeders, none of which have their own test:

| Seeder | Slice |
|---|---|
| `DefaultActivityRoleSeeder` | Core |
| `TimerPresetSeeder` | Core |
| `PomodoroTimerPresetSeeder` | Core |
| `ActivityExpectedCostTierSeeder` | ActivityProfiles |
| `ActivityExperienceTypeSeeder` | ActivityProfiles |
| `ActivityLocationTypeSeeder` | ActivityProfiles |
| `ActivityWeatherDependencySeeder` | ActivityProfiles |
| `CalendarSeeder` | Planning |
| `TaskImportanceSeeder` | Planning |
| `UserPlannerSettingsSeeder` | Planning |
| `RoutineTimePeriodSeeder` | Routines |
| `TaskPrioritySeeder` | TodoLists |

All implement `IPerUserDefaultSeeder` (`framework/Sydowwe.Framework/infrastructure/persistence/
seeder/interface/IPerUserDefaultSeeder.cs`) and run through `PerUserDefaultSeederManager`.

## What to write

Add `Seeding/PerUserDefaultSeederTests.cs` (integration, real DB via `PostgresTestBase`) with — at
minimum — one shared parametrized test run against every seeder in the table above:
- Seeding a fresh user produces the expected rows exactly once.
- Running the seeder a **second time for the same user** does not throw a `23505` duplicate-key
  violation and does not duplicate rows — this is the exact regression class `PerUserDefaultMatcher`
  was hardened against, and per-seeder coverage is the only thing that would catch a seeder calling the
  matcher incorrectly (wrong key selector, wrong entity type) even though the matcher itself is correct.
- Seeded rows are scoped to the target user only — running for user B doesn't touch user A's rows.

If any seeder has unique post-seed behavior beyond "insert defaults" (e.g. `CalendarSeeder` or
`UserPlannerSettingsSeeder` wiring cross-entity references), give it its own dedicated fact beyond the
shared parametrized case.

## Out of scope

Don't touch `DefaultUsersSeeder` (app-wide, not per-user) — already covered by
`Seeding/DefaultUsersSeederTests.cs`. Don't re-test `PerUserDefaultMatcher` in isolation — that's
`PerUserDefaultMatcherTests.cs`'s job; this task is per-seeder integration coverage.
