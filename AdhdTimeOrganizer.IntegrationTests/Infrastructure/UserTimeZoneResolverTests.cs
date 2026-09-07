using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Infrastructure;

/// <summary>
/// TEST-13 — <c>UserTimeZoneResolver</c>, the read every other user-scoped time calculation is resolved
/// from. Its effect is asserted in three dashboard suites, all of which use a user whose zone <b>is</b>
/// set; the two behaviours the class exists for had no test at all.
/// </summary>
/// <remarks>
/// Service-level rather than HTTP: the failure modes are internal to the class (which fallback, which
/// cache key) and no endpoint distinguishes them. All four cases pass today — they are guards.
/// </remarks>
[Collection("Postgres")]
public class UserTimeZoneResolverTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    /// <summary>
    /// A zone that is neither UTC nor, on a UTC build agent, the machine's own — picked from the running
    /// platform's database rather than hard-coded, because zone ids differ between Windows and the ICU
    /// data Linux uses.
    /// </summary>
    private static readonly TimeZoneInfo NonUtcZone =
        TimeZoneInfo.GetSystemTimeZones().First(z => z.BaseUtcOffset != TimeSpan.Zero);

    /// <summary>
    /// The fallback the contract's remarks are about: the answer must not depend on which machine the
    /// server happens to be running on.
    /// </summary>
    [Fact(DisplayName = "TEST-13: an unknown user id resolves to UTC, never the server's local zone")]
    public async Task UnknownUser_ResolvesToUtc()
    {
        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IUserTimeZoneResolver>();

        var result = await resolver.GetAsync(long.MaxValue, CancellationToken);

        result.Should().Be(TimeZoneInfo.Utc);

        // A no-op on a UTC CI box and the entire point of the test on a developer machine in
        // Europe/Bratislava, where a regression to TimeZoneInfo.Local looks correct everywhere else.
        // Do not delete it as redundant.
        if (TimeZoneInfo.Local != TimeZoneInfo.Utc)
            result.Should().NotBe(TimeZoneInfo.Local, "the answer must not depend on which machine the server runs on");
    }

    /// <summary>
    /// Also pins the value conversion in <c>UserEntityConfiguration</c>, which stores the zone by its id
    /// string: a conversion that silently failed would surface as UTC and look exactly like the fallback
    /// above passing.
    /// </summary>
    [Fact(DisplayName = "TEST-13: a user's stored zone comes back, not the fallback")]
    public async Task KnownUser_ResolvesTheirOwnZone()
    {
        var userId = await SeedUserAsync($"tz-own-{Guid.NewGuid():N}@test.com", NonUtcZone);

        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IUserTimeZoneResolver>();

        (await resolver.GetAsync(userId, CancellationToken)).Should().Be(NonUtcZone);
    }

    /// <summary>
    /// The test the class's own comment asks for by name — the one that fails if someone "simplifies"
    /// the dictionary into a single memoised field, which no request path in the app today would notice
    /// but every multi-user background handler would.
    /// </summary>
    [Fact(DisplayName = "TEST-13: the per-request cache is keyed by user, not a single field")]
    public async Task TwoUsersInOneScope_EachGetTheirOwnZone()
    {
        var utcUserId = await SeedUserAsync($"tz-a-{Guid.NewGuid():N}@test.com", TimeZoneInfo.Utc);
        var otherUserId = await SeedUserAsync($"tz-b-{Guid.NewGuid():N}@test.com", NonUtcZone);

        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IUserTimeZoneResolver>();

        (await resolver.GetAsync(utcUserId, CancellationToken)).Should().Be(TimeZoneInfo.Utc);
        (await resolver.GetAsync(otherUserId, CancellationToken)).Should().Be(NonUtcZone);
        (await resolver.GetAsync(utcUserId, CancellationToken)).Should().Be(TimeZoneInfo.Utc,
            "the cache has to store per user, not overwrite — the second lookup must not have evicted the first");
    }

    /// <summary>
    /// Trivially true today, because <c>User</c> is not covered by the per-user global filter — which is
    /// exactly the situation the code comment says must not be relied on. This is what stops a future
    /// filter on <c>User</c> from silently turning every non-self lookup into the UTC fallback.
    /// </summary>
    [Fact(DisplayName = "TEST-13: the zone read does not depend on the scoping it bootstraps")]
    public async Task ForeignUser_ResolvesWithoutBeingTheAmbientCaller()
    {
        var otherUserId = await SeedUserAsync($"tz-foreign-{Guid.NewGuid():N}@test.com", NonUtcZone);

        // The scope's ambient logged-in user is not this one; the read must still find the row.
        await using var factory = CreateFactory(["User"], FakeLoggedUserService.TestUserId);
        using var scope = factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IUserTimeZoneResolver>();

        (await resolver.GetAsync(otherUserId, CancellationToken)).Should().Be(NonUtcZone,
            "this is the read everything else's scoping is resolved from, so it cannot be subject to that scoping");
    }

    private async Task<long> SeedUserAsync(string email, TimeZoneInfo zone)
    {
        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = new User
        {
            Email = email, UserName = email,
            NormalizedEmail = email.ToUpperInvariant(), NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true, Locale = AvailableLocales.En, Timezone = zone
        };
        (await userManager.CreateAsync(user, "Test@1234!")).Succeeded.Should().BeTrue();
        return user.Id;
    }
}
