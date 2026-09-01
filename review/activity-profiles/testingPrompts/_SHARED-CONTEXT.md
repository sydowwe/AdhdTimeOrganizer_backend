# Shared context for every ActivityProfiles test prompt

Every prompt in this folder assumes this file. Read it once; the individual prompts do not repeat it.

## Where tests go

There is **no** `AdhdTimeOrganizer.ActivityProfiles.Tests` project and there must not be one — this
slice's failures are all composition failures, so its tests live with the host's.
Everything goes in `AdhdTimeOrganizer.IntegrationTests/Endpoints/`, namespace
`AdhdTimeOrganizer.IntegrationTests.Endpoints`, against a real Postgres through a real HTTP client.
The two pure-rule suites live in `AdhdTimeOrganizer.IntegrationTests/Services/` and need no database.

Existing files to read before writing anything:

| File | Why |
|---|---|
| `Endpoints/ActivityProfileGridTests.cs` | The seeding helpers (`:33-150`) and the cross-user pattern. Most prompts here extend this file or copy its preamble |
| `Endpoints/LeisureSuggestionTests.cs` | The leisure request/response shapes and its own seed helpers |
| `Endpoints/LeisureWeatherFitTests.cs` | `StubWeatherProvider` / `ThrowingWeatherProvider` (`:299`, `:310`) and how they are injected |
| `Endpoints/ActivityEndpointTests.cs` | How the lookup and `MemoryAnchor` CRUD suites subclass the framework base tests (`:392-790`) |
| `Endpoints/ActivityProfilesRouteSmokeTests.cs` | The wiring guards. Do **not** add behavioural assertions here |

## The standard file skeleton

```csharp
using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// One paragraph saying WHY these tests exist — specifically, what fails silently without them.
/// This suite's convention: the class comment names the silent-failure mode, and each
/// [Fact(DisplayName = "...")] states the behaviour actually observed, not the one assumed.
/// </summary>
[Collection("Postgres")]
public class SomethingTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact(DisplayName = "...")]
    public async Task Scenario()
    {
        long id;
        await using (var db = CreateDbContext())
            id = await /* seed */;

        var response = await CreateClient().PostAsJsonAsync("api/...", payload, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var verifyDb = CreateDbContext();   // ALWAYS a fresh context for assertions
        // assert on rows
    }
}
```

## What `PostgresTestBase` gives you

`framework/Sydowwe.Framework.Testing/PostgresTestBase.cs`:

| Member | Use |
|---|---|
| `CreateClient()` | Authenticated with Admin **and** User roles |
| `CreateUserRoleClient()` | Authenticated with only the `User` role — use this for anything role-sensitive |
| `CreateUnauthenticatedClient()` | For the 401 cases |
| `CreateFactory(roles, userId, configureServices, configOverrides)` | A whole app with a different test user id, replaced services, or overridden config. **You own it — dispose it.** This is how the weather doubles and every "act as the other user" scenario are done |
| `CreateDbContext()` | A fresh `DbContext` for seeding and verification. `await using` it |
| `JsonOpts` | The `JsonSerializerOptions` matching the app's FastEndpoints config — always pass it |
| `CancellationToken` | The test's token (equivalent to `TestContext.Current.CancellationToken`; prefer the base's property) |
| `Fixture.UnauthenticatedFactory.Services` | For resolving `UserManager<User>` etc. |

**Rule that is followed everywhere and must stay followed: verify on a fresh context.** Never assert
against the `DbContext` you seeded with — the change tracker makes a broken write look correct. This
matters more here than usual: several of these tests exist to prove a *query filter* behaves, and a
tracked entity is returned without ever running the filtered query.

## Seeding this slice

`ActivityProfileGridTests` has the only complete set of helpers. They are `private` today — **if your
prompt needs them, promote them into a new
`AdhdTimeOrganizer.IntegrationTests/Endpoints/ActivityProfileTestSeedHelper.cs` static class and have
`ActivityProfileGridTests` call it**, rather than making a fourth copy. There are already three
(`ActivityProfileGridTests:33-150`, `LeisureSuggestionTests`, `LeisureWeatherFitTests`).

The shape you need:

```csharp
long roleId     = /* new ActivityRole { UserId, Name, Color = "#112233" } */;
long activityId = /* new Activity { UserId, Name, RoleId } */;
long locationId = /* new ActivityLocationType { UserId, Text } */;      // + WeatherDependency, ExpectedCostTier
var profile = new ActivityBacklogProfile {
    ActivityId, LocationTypeId, WeatherDependencyId, ExpectedCostTierId,
    EnergyLevel = EnergyLevel.Low, MinParticipants = 1, DurationMinutes = 30, IsRepeatable = false };
```

Bucket list needs `ExperienceTypeId`, `ComfortZoneStep` (1–5), `InspirationSource`.
Project needs `DifficultyLevel`, `ProjectArea`, `EstimatedHours`, `ReadinessStatus` and **no**
lookups.

**A second user must be created through `UserManager<User>`**, the way
`ActivityProfileGridTests.CreateSecondUserAsync:33-50` does — not by inserting a `User` row. The
per-user default seeders fire on sign-up, and a hand-inserted user has no lookups, which makes
cross-user tests pass for the wrong reason.

## Three traps in this slice

1. **The three `Activity*Profile` entities are not `IEntityWithUser`.** There is no global user query
   filter on them (`AppDbContext.cs:299-306` gives them only `AppQueryFilters.ActivityArchived`), so
   ownership is reached through `p.Activity.UserId` by hand, per endpoint. A single-user test cannot
   see any of the failures these prompts are about — **always seed a second user.**
2. **`MemoryAnchor` has two check constraints** (`anchor_month BETWEEN 1 AND 12`,
   `rating BETWEEN 1 AND 10`) and a `(ActivityId, AnchorYear, AnchorMonth)` index. A hand-built row
   usually trips one for reasons unrelated to the test. Use the helper.
3. **`Activity` carries an archived query filter.** An activity seeded with `IsArchived = true`
   disappears from every read including its profiles — do not use it as the "should not be returned"
   mechanism when the thing under test is user scoping.

## Assert on rows, not on routes

Every load-bearing mechanism here fails with a 200: a deleted `ApplyUserScoping` override, an
unscoped `GetAll`, a lost `HasConstraintName`, a weather signal that matches nothing. Assert on the
**id set** or on the **rows in a fresh context**. `StatusCode.Should().NotBe(NotFound)` is only
acceptable in `ActivityProfilesRouteSmokeTests`, where registration is the subject.

## Pinning a known-bad behaviour

Several prompts below are expected to **fail** on today's code — they document real defects from
`review/activity-profiles/02-findings.md`. The established convention in this repo is a **green
regression pin against the correct behaviour**: fix the defect, write the test green. Only when the
fix is out of scope for the change at hand, write the test asserting the *correct* behaviour and mark
it:

```csharp
[Fact(DisplayName = "SEC-1: GET /activity-backlog-profile must not return another user's rows")]
[Trait("Status", "KnownGap")]
```

There is **no** `[Trait("Status","KnownGap")]` anywhere in the test project today, so the first
prompt to use one establishes the convention rather than following it — and the
`dotnet test --filter Status!=KnownGap` filter is not wired into any CI step. Check before relying on
it.
