# TEST-4 — `CalendarDayTypeRule`, both failure shapes

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

## Why

`application/helper/CalendarDayTypeRule.cs` has **one** implementation behind **two** callers with
deliberately different failure shapes — which is why it returns a message instead of throwing:

- `UpdateCalendarEndpoint.AfterMapping` turns a violation into a **400** via `ThrowError`.
- `BatchUpdateDayTypeCalendarEndpoint` turns the same violation into that one day's
  **`succeeded: false`**, so a selected week containing a Saturday does not fail the other five days.

Only the batch half is covered
(`CalendarBatchEndpointTests.BatchDayType_WeekendRuleFailsOneDayWithoutFailingTheBatch`). The 400 half —
the one an ordinary user hits from the day-edit dialog — is not.

The rule itself:

```csharp
DayType.Weekend  when !calendar.IsWeekend => "…only valid on Saturday or Sunday…",
DayType.Workday  when  calendar.IsWeekend => "…not valid on weekends…",
_ => null
```

`Calendar.IsWeekend` is `DayIndex is 6 or 7`, where `DayIndex` maps Sunday to **7**
(`Calendar.cs:35-37`).

## Where

`AdhdTimeOrganizer.IntegrationTests/Endpoints/` — extend `CalendarBatchEndpointTests` so both shapes sit
in one file, or add `CalendarDayTypeRuleTests` beside it.

## Request

```jsonc
PUT api/calendar/{id}
{
  "dayType": "Workday",                       // enum as string
  "label": null,
  "wakeUpTime": { "hours": 8, "minutes": 0 },
  "bedTime":    { "hours": 0, "minutes": 0 },
  "location": null,
  "weather": null,
  "notes": null
}
```

`CalendarRequest` has no `Date` — the day's date is `init`-only on the entity and never changes, which is
exactly why the rule cannot live in a FluentValidation validator (neither caller has the date in its
request).

## Scenarios

| Day seeded | `dayType` sent | Expect |
|---|---|---|
| a Saturday | `Workday` | **400**, message names the date and `Saturday` |
| a **Sunday** | `Workday` | **400** — the `DayIndex == 7` mapping; a `DayOfWeek`-based rewrite breaks Sunday first |
| a Wednesday | `Weekend` | **400** |
| a Saturday | `Weekend` | 200 |
| a Wednesday | `Workday` | 200 |
| a Saturday | `Vacation` | 200 — the `_ => null` arm; only two of five day types are constrained |
| a Wednesday | `SickDay` | 200 |
| a Wednesday | `Special` | 200 |

For every 400 case, **re-read the row** and assert `DayType` is unchanged. `UpdateCalendarEndpoint`
validates in `AfterMapping` — *after* `UpdateEntity` has already written to the tracked entity — and
relies on `ThrowError` aborting before `SaveChanges`. That ordering is the thing worth pinning; a
refactor that moves the save earlier would leave the 400 with the write already committed.

Also assert the **other** fields still round-trip on a successful update (`Label`, `Weather`, `Notes`,
`Location`, both times) — `docs/testing.md` records that `Location`, `Weather` and `Notes` have no
behavioural coverage at all, and this is the cheapest place to give them some.

## Cross-check

Both callers must produce the same *decision* for the same day and day type, differing only in how they
report it. A `[Theory]` running the eight rows above through **both** routes — asserting 400/200 on one
and `succeeded: false`/`true` on the other — is the version that keeps the single implementation honest.
