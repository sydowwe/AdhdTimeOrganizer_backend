# TEST-13 — the "missing user ⇒ UTC, never `TimeZoneInfo.Local`" fallback is asserted nowhere

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first. Related finding: `CQ-10` 🟡.

**Status today:** `UserTimeZoneResolver` has no direct test. Its *effect* is asserted in
`HistoryDashboardUserZoneTests`, `TrackingDashboardUserZoneTests` and
`QuietHoursUserZoneAndLateDeliveryTests` — all of which use a user whose `Timezone` **is** set. The two
behaviours the class exists for are untested.

## What the code does

`Core/application/service/UserTimeZoneResolver.cs:9-32`

```csharp
public class UserTimeZoneResolver(DbContext dbContext) : IUserTimeZoneResolver, IScopedService
{
    private readonly Dictionary<long, TimeZoneInfo> _cache = new();

    public async ValueTask<TimeZoneInfo> GetAsync(long userId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(userId, out var cached)) return cached;

        var timeZone = await dbContext.Set<User>()
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => u.Timezone)
            .FirstOrDefaultAsync(ct) ?? TimeZoneInfo.Utc;

        _cache[userId] = timeZone;
        return timeZone;
    }
}
```

Three claims, none pinned:

1. **A missing user resolves to `TimeZoneInfo.Utc`**, deliberately *not* `TimeZoneInfo.Local` — the
   contract's remarks (`domain/serviceContract/IUserTimeZoneResolver.cs:14-17`) say the answer "must
   not depend on which machine the server happens to be running on". On a developer box in
   Europe/Bratislava a regression to `.Local` would look correct in every existing test and be wrong in
   production.
2. **The cache is keyed by user id, not held as a single field** — the comment (`:11-13`) says a
   background handler resolving several users through one scope "must not be handed the first one's
   zone for all of them". Nothing tests the second user.
3. **`IgnoreQueryFilters()` is deliberate** (`:21-23`): this is the read everything else's scoping is
   resolved *from*, so it must not depend on the scoping it bootstraps.

## Create `Infrastructure/UserTimeZoneResolverTests.cs`

This one is service-level, not HTTP. Resolve the service from a scope rather than going through an
endpoint:

```csharp
[Collection("Postgres")]
public class UserTimeZoneResolverTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private IUserTimeZoneResolver Resolve(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IUserTimeZoneResolver>();
}
```

Build the scope from `Fixture.UnauthenticatedFactory.Services.CreateScope()` — the same way
`Seed.SecondUserAsync` reaches `UserManager<User>`.

All four cases below should **pass today**. They are guards, not gap pins.

### 1. A missing user resolves to UTC

```csharp
[Fact(DisplayName = "TEST-13: an unknown user id resolves to UTC, never the server's local zone")]
```

`await resolver.GetAsync(long.MaxValue)` → `TimeZoneInfo.Utc`.

Then the assertion that gives the test its value:

```csharp
result.Should().Be(TimeZoneInfo.Utc);
result.Should().NotBe(TimeZoneInfo.Local, "the answer must not depend on which machine the server runs on");
```

The second line is a no-op on a UTC CI box and the whole point of the test on a developer machine —
add a comment saying so, or someone will delete it as redundant.

### 2. A user's own zone is returned

Seed a second user with `Timezone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bratislava")` and
assert it comes back. Note `UserEntityConfiguration.cs:22-25` converts the zone to and from its `Id`
string, so this also pins that the round-trip works — a conversion that silently fails would surface as
UTC and look like case 1 passing.

⚠ Zone ids differ by platform. The suite runs against a Linux Postgres container but the **app** runs
on the test host, so on Windows `"Europe/Bratislava"` resolves only on .NET 6+ ICU. It does resolve —
`Seed.SecondUserAsync` already sets `TimeZoneInfo.Utc` and the seeder uses `TimeZoneInfo.Local` — but
if it turns out flaky, use `TimeZoneInfo.CreateCustomTimeZone` or pick the id via
`TimeZoneInfo.GetSystemTimeZones().First(...)` rather than hard-coding.

### 3. Two users in one scope get two different zones

```csharp
[Fact(DisplayName = "TEST-13: the per-request cache is keyed by user, not a single field")]
```

Seed users A (UTC) and B (a non-UTC zone). Through **one** scope and **one** resolver instance, call
`GetAsync(A)` then `GetAsync(B)`; assert each gets its own. Then call `GetAsync(A)` again and assert it
is still A's — proving the cache stores rather than overwrites.

This is the test the class's own comment asks for by name, and it is the one that fails if someone
"simplifies" the dictionary into a single memoised field.

### 4. The read ignores query filters

```csharp
[Fact(DisplayName = "TEST-13: the zone read does not depend on the scoping it bootstraps")]
```

Resolve the zone for a user who is **not** the ambient logged-in user of the scope. It must succeed.
Today `User` is not covered by the per-user global filter so this passes trivially — which is exactly
the situation the code comment says must not be relied on (`:21-23`: "this read must not start
depending on that"). The test is what stops a future filter on `User` from silently making every
non-self zone lookup return UTC.

## Related: `CQ-10`

While in this area: `DefaultUsersSeeder.cs:27` seeds the root admin with `Timezone = TimeZoneInfo.Local`
— the exact fallback this class refuses to have. Fixing it to `TimeZoneInfo.Utc` (or a
`ROOT_ADMIN_TIMEZONE` env var) is a one-line change, and `DefaultUsersSeederTests` is where its
assertion goes, not here.

## Doc reconciliation

None for the tests. If `CQ-10` is fixed, `../../docs/domain-map.md` → Business rules has a "A missing
user's time zone resolves to UTC" entry that could gain a clause about the seeded admin.
