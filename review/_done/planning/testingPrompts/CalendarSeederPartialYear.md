# TEST-22 — `CalendarSeeder`'s hand-rolled setup and its no-op reset

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

## Why

`CalendarSeeder` (`infrastructure/persistence/seeder/userDefault/CalendarSeeder.cs`) is the one seeder in
the slice that **does not** subclass `BasePerUserDefaultSeeder<T>` — its key is a date range rather than
a row set, so it hand-rolls `SetupDefaults` / `ResetDefaults` and does its own `IgnoreQueryFilters()`.
`docs/summary.md` says plainly: *"Deliberate; do not normalise it."*

Which means the shared guarantees `PerUserDefaultSeederTests` and `PerUserDefaultMatcherTests` give every
other per-user default seeder **do not apply here**, and nothing else covers it. Three behaviours are
load-bearing and unpinned.

## Where

`AdhdTimeOrganizer.IntegrationTests/Seeding/` — a new `CalendarSeederTests`, beside
`PerUserDefaultSeederTests`.

## Scenario A — fills per **missing date**, not per year

`SeedYearForUser:63-84` reads the user's existing dates into a `HashSet` and adds only the gaps. The
comment explains why: *"the unique index is `(user_id, date)`, so a year the user has partially deleted
has to be filled in rather than skipped whole or re-inserted whole."*

1. Run `SetupDefaults(userId)`.
2. Delete a handful of scattered days — say 5 March, 17 July, 31 December of the current year. (Pick days
   with no planner tasks; `planner_task.calendar_id` is `Restrict`.)
3. Run `SetupDefaults(userId)` **again**.

Assert: the three days are back, the year has its full complement of rows, **and** no `DbUpdateException`
was thrown. A "does the year have any rows at all" implementation passes step 1 and fails step 3 by
either skipping the gaps or colliding on the unique index — assert the row count for the year equals the
number of days in it, so both failure modes are caught.

## Scenario B — the rolling year window

`SeedYears()` returns `[thisYear, thisYear + 1]`, resolved when the seeder runs. It used to be a
hard-coded `{ 2025, 2026 }`, and `CalendarSeeder`'s own doc comment records what that cost: *"past the
last seeded year every date resolved to no calendar row, and with no create-calendar endpoint nothing in
the app could make one."*

Assert `SetupDefaults` produces rows for `DateTime.UtcNow.Year` **and** `+ 1`, and none for `+ 2` or
`- 1`. Derive the expectation from `DateTime.UtcNow.Year` — never a literal — so the test does not
silently start passing for the wrong reason in January.

Note this is hygiene, not the fix: lazy creation via `CalendarProvisioner` is what actually made dates
past the horizon plannable, and a rolling window only helps users created from now on.

## Scenario C — `ResetDefaults` is a deliberate no-op

`:110-116` logs a warning and returns `false`. Every other per-user default seeder resets to its
defaults; this one cannot, because "the default" is a year of dates rather than a row set.

Seed the year, mutate a day (change its `DayType`, `Label` and `WakeUpTime`), call
`ResetDefaults(userId)`.

Assert: it returns **`false`**, and the mutated day is **unchanged**. Both halves matter — a future
refactor that "helpfully" makes reset re-seed would wipe every user's day metadata across two years, and
the return value is the only signal the caller gets.

## Scenario D — hand-scoping

`SeedYearForUser` uses `IgnoreQueryFilters()` with an explicit `c.UserId == userId`, for the same reason
`CalendarProvisioner.FindAsync` does: the seeder is told which user to seed, which is not necessarily the
one signed in, and a filtered read would return no dates and re-insert the whole year onto
`(user_id, date)`.

Call `SetupDefaults(userIdB)` from a scope whose ambient logged user is **A**, where B already has a
partially filled year. Assert it fills only B's gaps and throws nothing. Then assert A's rows are
untouched.

This is the scenario that breaks if someone removes the `IgnoreQueryFilters()` — and it breaks as a
unique-index violation during **user registration**, which is a bad place to find out.

## Scenario E — shared metadata with the provisioner

`CalendarSeeder` and `CalendarProvisioner` both build rows through `CalendarDayFactory.Create`, and
`LazyCalendarCreationTests.ALazilyCreatedDay_CarriesTheSameMetadataTheSeederWouldHaveGivenIt` already
pins that they agree. Do not duplicate it — but do assert here that the seeder passes
`HolidayCalendar.DefaultCountryCode` through (`:37`), since the public `SeedYear(year, countryCode,
userId)` overload lets a caller pass something else and nothing checks what happens then.
