# TEST-3 — the calendar dashboard: day fold, range bounds, `TopN`, isolation

> Read `_SHARED-CONTEXT.md` first. Findings: `DOC-2` (UTC fold vs the Timezone invariant), `SEC-3`
> (no validator), `PERF-4`.

## Endpoint

`POST api/activity-history/dashboard/calendar` — `application/endpoint/CalendarActivityEndpoint.cs`.
It sits directly under `application/endpoint/`, outside `command/` and `query/`, because it is the one
dashboard that reads another slice's entity: it gets the calendar's days through Core's
**`ICalendarDayLookup`** seam, never by naming Planning's `Calendar`. Do not replace that lookup with a
direct query in the course of fixing anything here.

## Request / response

```jsonc
// CalendarActivityRequest — no validator exists (SEC-3)
{ "startDate": "2026-03-01", "endDate": "2026-03-07", "topN": 3 }
```

```jsonc
// List<CalendarActivityDaySummary>, ordered by date
[{ "id": 1, "date": "2026-03-01", "dayType": 0, "dayIndex": 0, "label": null, "holidayName": null,
   "wakeUpTime": { "hours": 7, "minutes": 0 }, "bedTime": { "hours": 23, "minutes": 0 },
   "totalSeconds": 5400, "sessionCount": 3,
   "topRoles": [{ "roleId": 4, "roleName": "Work", "color": "#112233", "totalSeconds": 3600 }] }]
```

One entry per day returned by `ICalendarDayLookup`, so the test must seed calendar days as well as
history — see `LazyCalendarCreationTests` / `CalendarBatchEndpointTests` for how calendars are created in
this suite, and reuse whichever helper they expose rather than inserting `Calendar` rows by hand from
History's test file.

## Existing coverage

Exactly one test: `HistoryDashboardGroupIdentityTests.Calendar_TopRolesCarryTheRoleId`. Everything below
is new.

## Scenarios

### 1. ⚠️ The day fold is UTC, and the invariant says it must be the user's (`DOC-2`)

`domain-map.md` → *Invariants* → *Timezone*: **"Every dashboard folds and bounds its range in
`User.Timezone`, never UTC."** This endpoint does neither. Lines 23-24 build the range with
`DateTimeKind.Utc` midnights, line 35 buckets with `DateOnly.FromDateTime(ah.StartTimestamp)` — the UTC
date — and the class never injects `IUserTimeZoneResolver` at all, unlike every sibling dashboard.

Test: create a user in `Pacific/Auckland` (UTC+12/+13) with a calendar covering 2026-03-10…2026-03-12 and
one history row at `2026-03-10T20:00:00Z` = **09:00 on 11 March** local. Request 10–12 March. Assert the
row's seconds land on the `2026-03-11` day summary.

It will land on `2026-03-10`. Then do the same with a `America/New_York` user and an
`02:00Z` row (= 21:00 the previous local day) so the drift is proven in both directions.

**When fixing:** the days come from `ICalendarDayLookup` keyed by `DateOnly` — those are the *user's*
calendar days. Folding history in UTC while joining against a user-day calendar is precisely what makes
the mismatch invisible, so both sides must move together. Follow the sibling pattern: inject
`IUserTimeZoneResolver`, bound with `WallClockZone.ToUtc(date, TimeOnly.MinValue, tz)`, and bucket with
`DateOnly.FromDateTime(WallClockZone.FromUtc(ah.StartTimestamp, tz))` (that is exactly what
`HistorySummaryTimeOfDayEndpoint.cs:59` does).

If the fix is not landing now, pin the current UTC behaviour with `[Trait("Status","KnownGap")]`, name
`DOC-2`, and add the row to `docs/testing.md` § *Known gaps*.

### 2. The range's own edges

- A row at exactly `startDate 00:00` local is included; a row one second before is not.
- A row at exactly `endDate` 23:59:59 local is included — the endpoint's `to` is
  `endDate.AddDays(1)` at midnight, exclusive, which is the right shape; assert it survives the zone fix.
- `startDate == endDate` returns exactly one day summary.
- `endDate < startDate` — today this yields an empty list rather than a 400 (`SEC-3`). Assert whichever
  the fix chooses, and prefer 400.

### 3. Days with no history still appear

Request a 3-day range with history on only one day. Assert **three** entries, with
`totalSeconds: 0`, `sessionCount: 0`, `topRoles: []` on the two empty ones — the response is driven by the
calendar days, not by the history rows, and a client indexing by day depends on that.

### 4. `topN`

- Seed four roles with distinct totals on one day, request `topN: 2`, assert the two largest in
  descending `totalSeconds` order.
- `topN` omitted defaults to 3 (`CalendarActivityRequest.TopN = 3`).
- `topN: 0` and a negative value — today both reach `Take()` unchecked. Assert the behaviour is
  deliberate; `SEC-3` argues for a `1..10` range rule in a new `CalendarActivityRequestValidator`.

### 5. Range bounds (`SEC-3`)

Request `startDate: 2000-01-01, endDate: 2030-01-01`. Today this is accepted and loads every row the
user owns with two joins. Assert a **400** once the validator exists, reusing
`DateRangeDto.MaxCustomRangeDays` rather than inventing a second limit. Before choosing the cap, check
what range the SPA's calendar view actually requests (`MIG-6`).

### 6. Auth / IDOR

Seed a second user with a calendar over the same dates and history on those dates. Assert their seconds
appear in neither `totalSeconds` nor `topRoles`, and that `sessionCount` counts only the caller's rows.
The endpoint filters `ah.UserId == userId` by hand *and* sits behind the global filter — this pins both.
Also assert the *calendar days* returned are the caller's own (the lookup takes `userId`).

### 7. `color` normalisation (nit)

Line 53 does `Color = g.First().Activity.Role.Color ?? ""` — an empty string where every other
group-shaped response passes `null` through. Seed a role with a null colour and assert whichever the team
decides; just make it consistent with `HistoryGroupKey`.

## Where to put it

New file `AdhdTimeOrganizer.IntegrationTests/Endpoints/HistoryCalendarDashboardTests.cs`. If scenario 1
lands as a fix rather than a pin, also add the calendar to `HistoryDashboardUserZoneTests`' guard row in
`AdhdTimeOrganizer.History/docs/testing.md`.
