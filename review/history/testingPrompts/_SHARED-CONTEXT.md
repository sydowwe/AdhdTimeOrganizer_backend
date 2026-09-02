# Shared context — History test prompts

Read this before any prompt in this folder. Every prompt assumes it and does not repeat it.

## Where tests go

`AdhdTimeOrganizer.History` has **no test project**. All of its tests live in
`AdhdTimeOrganizer.IntegrationTests`, almost all under `Endpoints/`, one file per concern. Add to an
existing history suite when the concern already has one; otherwise create a new file there. Namespace is
`AdhdTimeOrganizer.IntegrationTests.Endpoints`.

Existing history suites, for reference and for copying shape from:

| File | Covers |
|---|---|
| `HistoryCrudAuthMatrixTests.cs` | CRUD + auth matrix via Framework base test classes; the `file static class Seed` helpers |
| `HistoryRouteSmokeTests.cs` | route registration, the membership seam, `aggregate-by-activity` values |
| `HistoryDashboardGroupIdentityTests.cs` | `groupId` on every group-shaped response |
| `HistoryDashboardUserZoneTests.cs` | ranges/windows resolved in `User.Timezone`, DST gap |
| `HistoryTimeOfDayDashboardTests.cs` | the 24-bucket fold and its totals equality |
| `HistorySummaryCustomRangeTests.cs` | custom ranges, inclusive ends, max span |
| `TimerBoundaryAlarmTests.cs` | the alarm lifecycle; contains a `CapturingWebPushSender` |

## Harness

- Base class: `PostgresTestBase(AppDbContextFixture fixture)` from `Sydowwe.Framework.Testing`.
  Decorate the class with `[Collection("Postgres")]` — a real Postgres container shared across the
  collection.
- `CreateDbContext()` → an `await using` `DbContext` for seeding and verification. Use
  `db.Set<T>()`, never a typed `DbSet` property (History takes a plain `DbContext`).
- Clients: `CreateClient()` / `CreateUserRoleClient()` / `CreateAdminRoleClient()`, and
  `Fixture.UnauthenticatedFactory` for `UserManager<User>` work.
- The signed-in user id is `FakeLoggedUserService.TestUserId`. Its `Timezone` is **UTC** — a test that
  means to prove a user-zone fold must create its own non-UTC user.
- Cancellation: `TestContext.Current.CancellationToken`, or the base's `CancellationToken` property.
- JSON: `private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);` and
  `PostAsJsonAsync(route, body, JsonOpts, ct)`. Routes are prefixed **`api/`** (e.g.
  `api/activity-history/filter`).
- Assertions: FluentAssertions, with a `because` string on anything non-obvious. Prefer reading the
  response as `JsonElement` over defining a mirror DTO — the existing suites do this.

## Seeding helpers to reuse

`HistoryCrudAuthMatrixTests.cs` has a `file static class Seed` with `SecondUserAsync(fixture, email)`,
`ActivityAsync(db, userId, name)` and `HistoryAsync(db, userId, activityName, start?)`. It is `file`-scoped,
so it is **not** visible from another file: copy the three methods into your new suite (that is what
every existing suite does) rather than trying to share them. Always use a
`Guid.NewGuid():N`-suffixed email and activity name — the container is shared, and `Activity`,
`ActivityRole` and `ActivityCategory` each carry a unique index on `(UserId, Name)`.

`ActivityHistory` requires `UserId`, `ActivityId`, `StartTimestamp`, `Length` (`new IntTime(hours,
minutes)`) and `EndTimestamp`. **`StartTimestamp` must be `DateTimeKind.Utc`** — the column is
`timestamp with time zone` and Npgsql rejects any other kind. `(UserId, ActivityId, StartTimestamp)` is
unique, so vary the start or the activity between seeded rows.

## Two things that will bite you

1. **Materialized views.** Any save touching `ActivityHistory` fires the host's
   `SuggestionPatternRefreshInterceptor`, which `REFRESH`es three materialized views installed by
   `AppDbContextFixture.OnSchemaCreatedAsync`. A wave of `42P01` across unrelated tests means that
   fixture step broke, not your test.
2. ~~**The grid route is `gird`**, not `grid`.~~ **Withdrawn 2026-09-02.** The `gird` endpoint was
   deleted with `CQ-1`: it applied neither paging nor sorting, and — contrary to what this file, the
   findings doc and both slice docs claimed — **the SPA never called it** (`grep -ri gird` over the
   frontend repo: zero hits). `POST /activity-history/filter` is the only list read on this entity.

## Pinning a gap you cannot fix

If a test documents behaviour that is wrong and is not being fixed in the same change, pin it rather
than leaving it silent:

```csharp
[Fact]
[Trait("Status", "KnownGap")]
public async Task Something_IsWrongToday_AndThisPinsIt() { … }
```

Assert the **current** behaviour, and put the intended behaviour in the test's XML comment with the
review finding id (`CQ-3`, `SEC-1`, …). History carries no `KnownGap` tests today; any you add are the
first, so also add a row to `AdhdTimeOrganizer.History/docs/testing.md` § *Known gaps*.

## Assertion standard for this slice

`docs/testing.md` is blunt about it and it is the right bar: **every failure mode that matters in
History produces a well-formed 200 containing a plausible wrong number.** A status-code assertion proves
nothing. Prefer an equality between two surfaces that can only hold if the arithmetic is right — the
model is `HistoryTimeOfDayDashboardTests.TotalAcrossHours_MatchesThePieChartTotalForTheSameRange`.
