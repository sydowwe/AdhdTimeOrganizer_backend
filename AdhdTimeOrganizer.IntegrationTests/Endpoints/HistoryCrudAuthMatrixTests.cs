using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Sydowwe.Framework.Testing.baseTests;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-6 -- ActivityHistory CRUD auth matrix. ActivityHistory is IEntityWithUser and covered by
/// AppDbContext's global query filter (confirmed against domain-map.md), so every cross-user id here
/// 404s rather than 403s -- same reasoning as <see cref="ActivityEndpointTests"/>'s header, which this
/// follows in shape.
/// <para>
/// There is no grid case here. GetFilteredTableActivityHistoryEndpoint ("gird") was deleted with CQ-1 --
/// no client ever called it -- so FilterActivityHistoryEndpoint ("filter") below is the only list read
/// on this entity, and it carries the cross-user assertion the grid case used to hold.
/// </para>
/// </summary>
file static class Seed
{
    public const string Password = "Test@1234!";

    public static async Task<long> SecondUserAsync(IPostgresFixture fixture, string email)
    {
        using var scope = fixture.UnauthenticatedFactory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = new User
        {
            Email = email,
            UserName = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            Locale = AvailableLocales.En,
            Timezone = TimeZoneInfo.Utc
        };
        var result = await userManager.CreateAsync(user, Password);
        result.Succeeded.Should().BeTrue();
        return user.Id;
    }

    public static async Task<long> ActivityAsync(DbContext db, long userId, string name)
    {
        var role = new ActivityRole { UserId = userId, Name = $"{name} Role {Guid.NewGuid():N}", Color = "#112233" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var activity = new Activity { UserId = userId, Name = name, RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return activity.Id;
    }

    public static async Task<long> HistoryAsync(DbContext db, long userId, string activityName, DateTime? start = null)
    {
        var activityId = await ActivityAsync(db, userId, activityName);
        var startTimestamp = start ?? DateTime.UtcNow.Date.AddHours(10);
        var history = new ActivityHistory
        {
            UserId = userId,
            ActivityId = activityId,
            StartTimestamp = startTimestamp,
            Length = new IntTime(0, 30),
            EndTimestamp = startTimestamp.AddMinutes(30)
        };
        db.Set<ActivityHistory>().Add(history);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return history.Id;
    }
}

// =====================================================================================================
// ActivityHistory CRUD
// =====================================================================================================

[Collection("Postgres")]
public class ActivityHistoryGetByIdTests(AppDbContextFixture fixture) : BaseGetByIdEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-history";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.HistoryAsync(db, FakeLoggedUserService.TestUserId, "GetById History");

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"history-getbyid-{Guid.NewGuid():N}@test.com");
        return await Seed.HistoryAsync(db, otherUserId, "Foreign History");
    }
}

[Collection("Postgres")]
public class ActivityHistoryCreateTests(AppDbContextFixture fixture) : BaseCreateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-history";
    protected override bool IsAdminOnly => false;

    protected override async Task<object> BuildValidPayloadAsync(DbContext db)
    {
        var activityId = await Seed.ActivityAsync(db, FakeLoggedUserService.TestUserId, $"Create Activity {Guid.NewGuid():N}");
        return new
        {
            ActivityId = activityId,
            StartTimestamp = DateTime.UtcNow.Date.AddHours(9),
            Length = new { Hours = 0, Minutes = 30 }
        };
    }
}

[Collection("Postgres")]
public class ActivityHistoryUpdateTests(AppDbContextFixture fixture) : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-history";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.HistoryAsync(db, FakeLoggedUserService.TestUserId, "Update History");

    protected override async Task<object> BuildValidPayloadAsync(DbContext db, long id)
    {
        var activityId = await Seed.ActivityAsync(db, FakeLoggedUserService.TestUserId, $"Update Activity {Guid.NewGuid():N}");
        return new
        {
            ActivityId = activityId,
            StartTimestamp = DateTime.UtcNow.Date.AddHours(11),
            Length = new { Hours = 1, Minutes = 0 }
        };
    }

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"history-update-{Guid.NewGuid():N}@test.com");
        return await Seed.HistoryAsync(db, otherUserId, "Foreign Update History");
    }
}

[Collection("Postgres")]
public class ActivityHistoryDeleteTests(AppDbContextFixture fixture) : BaseDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-history";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.HistoryAsync(db, FakeLoggedUserService.TestUserId, "Delete History");

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"history-delete-{Guid.NewGuid():N}@test.com");
        return await Seed.HistoryAsync(db, otherUserId, "Foreign Delete History");
    }
}

// =====================================================================================================
// FilterActivityHistoryEndpoint ("api/activity-history/filter") -- ApplyUserScoping is a no-op that
// only resolves the caller's timezone; the row-level scoping this test cares about comes entirely from
// the global IEntityWithUser query filter, same as the CRUD routes above.
// =====================================================================================================

[Collection("Postgres")]
public class ActivityHistoryFilterTests(AppDbContextFixture fixture) : BaseFilterEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-history/filter";
    protected override bool IsAdminOnly => false;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.HistoryAsync(db, FakeLoggedUserService.TestUserId, "Filter History");

    // ActivityHistoryDetailFilter (DateAndTimeRangeDto) requires Date/From/To -- a bare {} payload
    // would 400 before ApplyCustomFiltering ever runs. Seed.HistoryAsync anchors StartTimestamp at
    // today 10:00 UTC, so a full-day window on today's date always contains it; the test user's
    // Timezone is UTC (AppDbContextFixture), matching ToDateTimeRange's zone.
    protected override object EmptyFilterPayload() => new
    {
        Date = DateOnly.FromDateTime(DateTime.UtcNow),
        From = new { Hours = 0, Minutes = 0 },
        To = new { Hours = 23, Minutes = 59 }
    };

    [Fact]
    public async Task Filter_ExcludesOtherUsersHistory()
    {
        await using var db = CreateDbContext();
        var ownId = await Seed.HistoryAsync(db, FakeLoggedUserService.TestUserId, "Own Filter History");

        var otherUserId = await Seed.SecondUserAsync(Fixture, $"history-filter-{Guid.NewGuid():N}@test.com");
        await Seed.HistoryAsync(db, otherUserId, "Foreign Filter History");

        var response = await CreateClient().PostAsJsonAsync(EndpointUrl, EmptyFilterPayload(), JsonOpts, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts, CancellationToken);
        items!.Select(i => i.GetProperty("id").GetInt64())
            .Should().Contain(ownId, "the caller's own row must be returned")
            .And.HaveCount(1, "another user's row must never leak into the filter results");
    }
}
