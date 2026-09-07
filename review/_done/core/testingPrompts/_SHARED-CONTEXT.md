# Shared context — AdhdTimeOrganizer.Core test prompts

Read this once before any sibling prompt in this folder. Everything here is verified against the repo
as of 2026-08-31; the prompts assume it and do not repeat it.

## Where tests go

`AdhdTimeOrganizer.Core` **has no test project**. Every test for it lives in
`AdhdTimeOrganizer.IntegrationTests/` — deliberately, because nearly every rule Core owns is a
property of the composed app (the `DbContext` → `AppDbContext` alias, the global query filters, the
two DI scans, the seam registrations) and none of it is observable from a library-only unit test.

- Endpoint behaviour → `AdhdTimeOrganizer.IntegrationTests/Endpoints/<Name>Tests.cs`
- Model / configuration / wiring → `…/Modules/` or `…/Infrastructure/`
- Seeding → `…/Seeding/`

One class per concern in its own file. Do **not** add to `Endpoints/ActivityEndpointTests.cs` — it is
already ~1100 lines and spans two projects.

## Test base and fixture

```csharp
[Collection("Postgres")]
public class MyTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
```

`PostgresTestBase` (from `Sydowwe.Framework.Testing`) gives you, all already wired:

| Member | What it is |
|---|---|
| `CreateClient()` | `HttpClient` authenticated as `FakeLoggedUserService.TestUserId` with role `User` |
| `CreateAdminRoleClient()` / `CreateUserRoleClient()` | role-specific clients |
| `CreateFactory(string[] roles, long userId)` | a whole factory acting as another user — `await using var factoryB = CreateFactory(["User"], otherUserId); factoryB.CreateClient()` |
| `Fixture.UnauthenticatedFactory` | no auth header; also the way to reach `UserManager<User>` |
| `CreateDbContext()` | a real `AppDbContext` (`await using`), **with global query filters on** — add `.IgnoreQueryFilters()` when asserting on rows an endpoint is supposed to have hidden or destroyed |
| `CancellationToken` | use it, or `TestContext.Current.CancellationToken` |
| `JsonOpts` | the app's `JsonSerializerOptions` — always pass it to `PostAsJsonAsync` / `ReadFromJsonAsync` |

`FakeLoggedUserService.TestUserId` is the caller every `CreateClient()` acts as. All routes are under
`api/` (`config.Endpoints.RoutePrefix = "api"`).

## Seeding

`Endpoints/ActivityEndpointTests.cs` has a `file static class Seed` — **`file`-scoped, so you cannot
reference it.** Copy the shape into a private helper in your own file:

```csharp
private static long UserId => FakeLoggedUserService.TestUserId;

private async Task<long> SeedRoleAsync(DbContext db, long userId, string name)
{
    var role = new ActivityRole { UserId = userId, Name = name, Color = "#112233" };
    db.Set<ActivityRole>().Add(role);
    await db.SaveChangesAsync(CancellationToken);
    return role.Id;
}

private async Task<long> SeedActivityAsync(DbContext db, long userId, string name, bool isArchived = false)
{
    var roleId = await SeedRoleAsync(db, userId, $"{name} Role {Guid.NewGuid():N}");
    var activity = new Activity { UserId = userId, Name = name, RoleId = roleId, IsArchived = isArchived };
    db.Set<Activity>().Add(activity);
    await db.SaveChangesAsync(CancellationToken);
    return activity.Id;
}
```

A **second user** (needed by several prompts):

```csharp
using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
var user = new User
{
    Email = email, UserName = email,
    NormalizedEmail = email.ToUpperInvariant(), NormalizedUserName = email.ToUpperInvariant(),
    EmailConfirmed = true, Locale = AvailableLocales.En, Timezone = TimeZoneInfo.Utc
};
(await userManager.CreateAsync(user, "Test@1234!")).Succeeded.Should().BeTrue();
return user.Id;
```

⚠ **Always suffix seeded names with `Guid.NewGuid():N`.** The Postgres container is shared across the
whole collection, and `(user_id, name)` is unique on `Activity`, `ActivityRole` and `ActivityCategory`.

## Uniqueness and required fields worth knowing

- `Activity`: unique `(UserId, Name)`; `RoleId` **required**, `CategoryId` optional.
- `ActivityRole` / `ActivityCategory`: unique `(UserId, Name)`. `Color` is required, `#RRGGBB`.
- `ActivityRole`: additionally unique `(UserId, SystemKey)` **filtered to `system_key IS NOT NULL`**.
- All three profile tables have a **unique** `ActivityId` (they are 1:1 with `Activity`).
- Every FK into `Activity` is `Cascade` except `TodoListItem.PairedLeisureActivityId` (`SetNull`).

## The `ActivityArchived` filter

`AppQueryFilters.ActivityArchived` is a **named** global filter declared host-side in `AppDbContext`
on `Activity` and mirrored onto `ActivityBacklogProfile`, `ActivityBucketListProfile`,
`ActivityProjectProfile` and the two `PlannerSuggestionFrom*` view entities. In a test, lift it with
`.IgnoreQueryFilters()` (all filters) or
`.IgnoreQueryFilters([AppQueryFilters.ActivityArchived])` (just this concern —
`using AdhdTimeOrganizer.infrastructure.persistence;`, note the **host** namespace, not `.Core.`).

## Assertion style

**Assert on rows, not on routes.** Almost every mechanism in Core fails silently — a missing seam
registration lowers a count, a dropped assembly 404s a route, a doubled DI scan runs a seeder twice —
and a status-code assertion catches none of it. `ActivityMergeTests.Merge_RepointsEveryReferencingSlice`
is the model to copy: it asserts on surviving rows in six projects, not on the count the endpoint
returned.

Use FluentAssertions and give every non-obvious assertion a `because` string:

```csharp
rows.Should().BeEmpty("deleting a category must blank the classification, not destroy the activity");
```

## Pinning a test that is expected to fail today

Several prompts describe behaviour the code does **not** have yet. Do not write the test to pass
against current behaviour — write it to assert the correct behaviour and pin it:

```csharp
[Fact(DisplayName = "SEC-1: deleting a category does not destroy the activities under it")]
[Trait("Status", "KnownGap")]
```

Each prompt says explicitly which of its cases is a `KnownGap` and which should pass today. When the
underlying finding is fixed, the trait comes off in the same commit.

## Running

```
dotnet test AdhdTimeOrganizer.IntegrationTests
```

Needs Docker (Testcontainers Postgres). Filter with
`--filter "FullyQualifiedName~ActivityLookupDelete"`.

## Finding IDs

`SEC-n` / `CQ-n` / `PERF-n` / `DOC-n` referenced by these prompts are defined in
[`../02-findings.md`](../02-findings.md). Quote the ID in the test's `DisplayName` so the two stay
linked.
