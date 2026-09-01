# Shared context for every Tracking test prompt

Read this first; each prompt in this folder assumes it and does not repeat it.

## Where things live

- **Module under test:** `AdhdTimeOrganizer.Tracking` (a vertical slice). Its living docs are
  `AdhdTimeOrganizer.Tracking/docs/summary.md`, `docs/domain-map.md` and `docs/testing.md` — read
  `summary.md` before writing anything, and use `domain-map.md`'s navigation index to find files.
- ⚠ **Folders and namespaces have drifted.** The namespaces still carry an `activityTracking` segment
  the folders no longer have, e.g. `AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop.DesktopActivityEntry`
  while the file is at `domain/model/entity/desktop/`. **Copy the `using` block from a neighbouring
  tracking test rather than guessing.**
- **Test project:** `AdhdTimeOrganizer.IntegrationTests` — this slice has no test project of its own,
  and `docs/testing.md` explains why that is right. Tracking tests live in `Endpoints/`.
- **Route prefix is `/api`** (`config.Endpoints.RoutePrefix = "api"`), so a route declared as
  `Post("/activity-tracking/desktop/pie-chart")` is called as
  `/api/activity-tracking/desktop/pie-chart`.

## Test infrastructure to reuse — do not reinvent

```csharp
[Collection("Postgres")]
public class MyTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
```

- `PostgresTestBase` gives you `CreateDbContext()`, `CreateClient()`, `CreateUserRoleClient()`,
  `Fixture`, `CancellationToken` and an overridable `SeedAsync(DbContext db)`.
- Pass `TestContext.Current.CancellationToken` to HTTP calls (that is the shipped convention here),
  and the base's `CancellationToken` to EF calls.
- Seed in a **disposed** context, then act over HTTP, then verify through a **fresh**
  `CreateDbContext()`. A tracked graph left over from seeding masks a bad query shape.
- Assertions are FluentAssertions (`.Should()`); the framework is xUnit v3.

### A second user must be a real row

`user_id` is NOT NULL with an FK. Copy the pattern from
`TrackingRouteSmokeTests.WebExtensionActivityEntry_KeepsBothOfItsQueryFilters:159-180`: pick
`FakeLoggedUserService.TestUserId + 9_000`, insert a `User` with `Email`, `NormalizedEmail`,
`UserName`, `NormalizedUserName`, `SecurityStamp`, `ConcurrencyStamp`, `Locale` and `Timezone` set,
and guard it with `IgnoreQueryFilters().AnyAsync(...)` so re-runs are idempotent.
`ReminderSeedHelper.EnsureOtherUserAsync(db, ct)` / `ReminderSeedHelper.OtherUserId` also work.

### Fixture-built contexts have no user scoping

`CreateDbContext()` builds straight off a `DbContextOptionsBuilder`, so it has no application service
provider — `UserScopingOptions` resolves to null and **no per-user query filter is applied to any
entity**. That is what makes cross-user seeding possible. It also means a scoping assertion has to go
through HTTP, or through the host-built model:
`Fixture.UnauthenticatedFactory.Services.CreateScope()` → `GetRequiredService<AppDbContext>()`.

### Extension-client tokens (ingest endpoints only)

`ExtensionActivityTrackingTests` derives from `AuthTestBase`, not `PostgresTestBase`, and owns the
whole extension-token flow: `CreateUserWithExtensionAccess(email, hasExtensionAccess, hasRole)` →
`ExtensionLoginSuccessAsync(email)` → an `HttpClient` with the bearer token. **Copy those three
helpers (`:308-364`) rather than re-deriving them.** Notes:

- `X-Forwarded-For` must be set to a fresh GUID on the login request — the login endpoint is rate
  limited per IP.
- An extension token carries the `User` role **and** the `ActivityTracking` role, the latter granted by
  `ExtensionRoleClaimsProvider` only when `User.HasExtensionAccess` is true.
- A plain web JWT (`CreateUserRoleClient()`) is *supposed* to get a 403 on an endpoint carrying
  `Policies(PortalAuthorizationPolicies.ActivityTracking)`. A 403 in a new ingest test is often the
  test's fault, not the endpoint's.

## Request-shape traps that have already bitten this suite

Both are documented in `TrackingRouteSmokeTests`' XML comments; do not rediscover them.

- **`from` / `to` on every dashboard request are `TimeDto` (`{hours, minutes}`), not `DateTime`.** They
  are a time-of-day window repeated on **each day** of the `dateFrom`..`dateTo` span, not the ends of a
  range. `dateFrom`/`dateTo` are `required`, so omitting either is a bind failure (400) before any
  validator runs. Copy `DashboardBody()` from `TrackingRouteSmokeTests:305-315`.
- **Grid bodies must send `filter: null`, not `filter: {}`.** `BaseFilterRequest.Filter` is
  `required TFilter?`; both mapping filters declare a `required TrackerDesktopMappingTypeEnum Type`,
  which System.Text.Json enforces at deserialization, so `{}` 400s. Copy `GridBody()` from
  `TrackingRouteSmokeTests:328-335`.
- **`baseline` is an enum and the suite's `JsonOpts` has no string-enum converter.** Send the integer,
  or omit it and rely on the DTO default (`Last7Days`). `BaselineType` is
  `Last7Days=0, Last30Days=1, SameWeekday=2, AllTime=3`.

## Seeding the ledgers

- `DesktopActivityEntry` and `WebExtensionActivityEntry` set `RecordDate` from `WindowStart` in the
  initializer — **do not set `RecordDate`**, it has a private setter. `WindowStart` must be UTC-kind
  and minute-aligned.
- Both are **partitioned** on `record_date` with yearly partitions 2026–2030 plus a default. Seed
  instants inside those years unless the test is about the default partition.
- Unique indexes to respect while seeding:
  `desktop_activity_entry (user_id, window_start, record_date, process_name, window_title)`,
  `web_extension_activity_entry (user_id, window_start, domain, record_date)`,
  `android_session_data (user_id, device_id, package_name, session_start_utc)`.
- Mapping rows: one pattern per user, `NULLS NOT DISTINCT` — two rules that both leave `ProcessName`
  null collide. Five check constraints apply; the important one is
  `CK_Tracker*MappingByPattern_TargetRequired` (exactly one of `is_ignored = true` / `activity_id` /
  `role_id`-or-`category_id`).
- An `Activity` needs an `ActivityRole`. Copy the seed block from
  `ExtensionActivityTrackingTests:206-222`.

## Timezone

**Dashboard tests need a user whose `Timezone` is not UTC**, or a correct fold is indistinguishable
from a missing one. Copy `SeedAsync` from `TrackingDashboardUserZoneTests:40-45`
(`Europe/Bratislava`), and note its comment about choosing a band width (90 minutes) that does not
divide evenly into the offset — with 30 or 60 the bug hides.

## Conventions

- **Assert on numbers and rows, not on status codes.** Every failure mode that matters in this module
  returns a well-formed 200 containing a plausible wrong number. A 200 proves nothing. Where possible
  assert an **equality between two surfaces** that can only hold if the arithmetic is right — the
  unified suite's `sum(countedSeconds) == totals.totalSeconds` is the model.
- **Check what already exists first.** `docs/testing.md` lists every guard and what it pins; several
  tracking behaviours are covered from files without "Tracking" in the name
  (`ActivityTimeAutomationTests`, `TrackerPatternMappingActivityFkTests`, `SeamWiringTests`,
  `ModuleSchemaTests`, `NamedQueryFilterTests`, `UserDataExportAndDeletionTests`).
- **Pinning a known-but-unfixed gap:** mark the test `[Trait("Status", "KnownGap")]`, assert the
  *current* behaviour, and put the finding ID and the intended behaviour in the XML doc. Do not leave
  a red test behind. No tracking test carries this trait today.
- ⚠ **`Npgsql.EnableLegacyTimestampBehavior` is on in tests and off in production**
  (`framework/Sydowwe.Framework.Testing/PostgresContainerFixture.cs:26`). A `DateTime` with
  `Kind=Unspecified` is silently accepted here and **throws** against a `timestamp with time zone`
  column in production. Always construct instants with `DateTimeKind.Utc`; a test that passes on an
  `Unspecified` value has proved nothing about production. See `CQ-1` / `MIG-5` in
  `review/tracking/02-findings.md`.
