# TEST-19 — `HolidayCalendar`: the tables and Computus

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

## Why

`domain/service/HolidayCalendar.cs` has **zero tests**. It is pure, static, dependency-free arithmetic —
the cheapest thing in the slice to test and currently the least tested. It feeds `CalendarDayFactory`,
which feeds **both** creators of a calendar row (`CalendarSeeder` for whole years,
`CalendarProvisioner` on first task), so an error here is written into every user's calendar table and
is only visible if someone looks at a specific date.

`Veľký piatok` and `Veľkonočný pondelok` are computed from `CalculateEaster(year)` — a Computus
implementation. Nothing checks it against a single known Easter date.

## Where

`AdhdTimeOrganizer.IntegrationTests/Services/HolidayCalendarTests.cs` — plain xUnit, **no** Postgres,
**no** `[Collection("Postgres")]`. It sits beside `PlannerStreakServiceTests` and
`PlanVsActualTrendServiceTests`, the other two pure-service suites.

## Encoding warning

The holiday names are Slovak and Czech, full of diacritics (`Deň vzniku Slovenskej republiky`,
`Sviatok svätého Cyrila a Metoda`, `Výročie SNP`). Write the test file as **UTF-8 without a BOM** using
your editor's write tool. Do **not** round-trip it through the shell — this machine's ANSI codepage is
1252 and a `Get-Content | Out-File` cycle double-encodes every one of these strings (`ň` → `Åˆ`) while
the build still passes. See the rule in the user's global `CLAUDE.md`.

If diacritics are awkward, assert `Should().NotBeNullOrEmpty()` on the name and match the **date** rather
than the text for the fixed holidays — the dates are what the code gets wrong, not the strings. Keep the
exact-string assertion for one or two entries only.

## Part 1 — Computus

`CalculateEaster` is the only nontrivial code in the file. Assert against known Easter Sundays
(Gregorian):

| Year | Easter Sunday |
|---|---|
| 2024 | 31 March |
| 2025 | 20 April |
| 2026 | 5 April |
| 2027 | 28 March |
| 2028 | 16 April |
| 2029 | 1 April |
| 2030 | 21 April |
| 2038 | 25 April (the latest possible date — the anomalous-year branch) |
| 2035 | 25 March (near the earliest possible, 22 March) |

`CalculateEaster` is private, so drive it through the public surface:
`ForDate(easterSunday.AddDays(-2), "SK")` must be `"Veľký piatok"` and
`ForDate(easterSunday.AddDays(1), "SK")` must be `"Veľkonočný pondelok"`. That covers the arithmetic and
the two derived entries at once.

Include 2038 and 2035 specifically — a Computus that is right for the common years and wrong at the
boundaries is the usual failure mode.

## Part 2 — the fixed tables

For **SK**, one `[Theory]` case per entry, asserting `ForDate(new DateOnly(year, m, d), "SK")` is
non-empty, across at least three years including a leap year (2028):

`01-01`, `01-06`, `05-01`, `05-08`, `07-05`, `08-29`, `09-01`, `09-15`, `11-01`, `11-17`, `12-24`,
`12-25`, `12-26` — thirteen fixed entries.

Do the same for **CZ** against whatever `Czech(year)` contains (read it; it was truncated in the review
pass and is not reproduced here).

## Part 3 — the contract

- `ForDate` on an ordinary day (e.g. `2029-03-03`) returns **null**.
- `ForYear` with an unknown country code returns an **empty dictionary**, not null and not a throw —
  the `_ => []` arm.
- Country code is case-insensitive: `"sk"`, `"SK"` and `"Sk"` agree (`ToUpperInvariant()` at `:16`).
- `DefaultCountryCode` is `"SK"`, and `ForDate(date)` with no code agrees with `ForDate(date, "SK")`.
  The constant's own doc says *"One place, so the two callers cannot drift"* — this is the assertion that
  keeps that true.
- **No duplicate-key throw in any year.** `Slovak()` builds a dictionary literal and then calls
  `holidays.Add(...)` twice for the Easter dates — if Easter Monday ever collides with a fixed holiday,
  `Add` throws `ArgumentException` and **every calendar creation for that year fails**. Easter Monday can
  fall in a window that includes no fixed SK holiday, so this should never fire; assert it anyway by
  looping `ForYear(y)` over, say, 2020–2060 and asserting no throw. That loop is four lines and covers a
  failure mode that would take out user registration.

## Part 4 — the integration end

One test in `Endpoints/`, or an added case to
`LazyCalendarCreationTests.ALazilyCreatedDay_CarriesTheSameMetadataTheSeederWouldHaveGivenIt`:

Create a planner task by `date` on **25 December** of a year past the seeded horizon, and assert the
lazily created `Calendar` carries the `HolidayName` **and** a `DayType` of `Workday` — not a `Holiday`
day type, which does not exist in the enum (`DOC-2`). `CalendarDayFactory.cs:35-37` states the rule:
*"a holiday on a Tuesday is still a Workday row with a HolidayName."*
