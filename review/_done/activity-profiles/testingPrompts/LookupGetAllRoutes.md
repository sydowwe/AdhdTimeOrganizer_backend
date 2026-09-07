# TEST-6 — the four lookups' `GetAll` routes

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

The four activity lookups have `GetById`, `Grid`, `Create`, `Update` and `BatchDelete` suites in
`ActivityEndpointTests.cs` (`:519-790`) and **no** `GetAll` suite. That is 4 of the slice's 57
endpoints with no coverage of any kind — including no proof that they are routed at all, since
`ActivityProfilesRouteSmokeTests` only probes `/filtered-table`.

## The endpoints

| Route | File |
|---|---|
| `GET /api/activity-location-type` | `application/endpoint/lookup/locationType/query/GetAllActivityLocationTypeEndpoint.cs` |
| `GET /api/activity-weather-dependency` | `.../weatherDependency/query/GetAllActivityWeatherDependencyEndpoint.cs` |
| `GET /api/activity-expected-cost-tier` | `.../expectedCostTier/query/GetAllActivityExpectedCostTierEndpoint.cs` |
| `GET /api/activity-experience-type` | `.../experienceType/query/GetAllActivityExperienceTypeEndpoint.cs` |

All four are one-liners over `BaseGetAllLookupEndpoint<T>`. Response is
`List<LookupResponse<T>>` — `{ "id", "text", "sortOrder", ... }`. These entities **are**
`IEntityWithUser`, so scoping comes from the global query filter rather than from any code in this
slice; the test's job is to prove that filter reaches this route, which is not obvious given
`BaseGetAllEndpoint` has no scoping of its own (see `SEC-1`).

## How to write it

Use the framework base test class, the way the sibling suites do:
`framework/Sydowwe.Framework.Testing/baseTests/BaseGetAllEndpointTests.cs`.

```csharp
[Collection("Postgres")]
public class ActivityLocationTypeGetAllTests(AppDbContextFixture fixture) : BaseGetAllEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-location-type";
    protected override bool IsAdminOnly => false;   // these are User-role routes
    protected override async Task<long> SeedEntityAsync(DbContext db) { /* new ActivityLocationType { UserId = FakeLoggedUserService.TestUserId, Text = "…" } */ }
}
```

`IsAdminOnly` defaults to **true** in the base (`:21`) and would assert a 403 for the `User` role —
wrong for these four, which the global `Roles("User","Admin","Root")` configurator opens to users.
Copy `ActivityLocationTypeGridTests:671` for how the sibling suites set their overrides.

That gives you happy path, User-role access and the 401 for free. Add one hand-written test per
family — or one `[Theory]` over the four routes:

- **Another user's lookup rows are not returned.** Seed a row for a second user (created through
  `UserManager`, so their default seeder rows exist too), `GET` as the caller, assert the second
  user's id is absent. This is the one that proves the global filter is doing the work, and the one
  a future `IgnoreQueryFilters` "fix" would break.

Note that the second user arrives with a **full set of seeded defaults** ("Indoor", "Outdoor", "Any"
etc. — `ActivityLocationTypeSeeder.cs:17-22`), so assert on ids, never on names or counts: both users
legitimately have a row called "Indoor".
