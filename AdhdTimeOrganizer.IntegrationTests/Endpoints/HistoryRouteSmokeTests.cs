using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The History counterpart of <see cref="CoreRouteSmokeTests"/> / <c>TodoListsRouteSmokeTests</c>,
/// plus the one behavioural check the History extraction actually needed.
/// <para>
/// The trap is shared with every slice: the endpoints only route if
/// <c>AdhdTimeOrganizer.History</c> is in the FastEndpoints <c>o.Assemblies</c> list in
/// <c>Program.cs</c> (<c>DisableAutoDiscovery = true</c>), and leaving it out is not a build error —
/// every history route simply 404s.
/// </para>
/// <para>
/// This class used to carry <c>Grid_MembershipFilter_NarrowsThroughTheSeam</c>, which drove Core's
/// <c>IActivityMembershipSource</c> through the <c>grid</c> endpoint. That endpoint is gone (<c>CQ-1</c>
/// — no client ever called it), so the seam's narrowing behaviour is now pinned directly on the source
/// in <c>SeamWiringTests.TodoListMembershipSource_NarrowsToActivitiesOnAList</c>.
/// </para>
/// </summary>
[Collection("Postgres")]
public class HistoryRouteSmokeTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The GET family. Deliberately only <c>form-select-options</c>: <c>GET /activity-history/{id}</c>
    /// answers 404 for a row that does not exist, which is indistinguishable from the 404 of a route
    /// that was never registered — the exact failure this suite exists to detect. An assertion that
    /// cannot tell the two apart is worse than no assertion.
    /// <para>
    /// Note the path is <c>form-select-options</c>, from <c>BaseActivityFormSelectOptionsEndpoint</c> —
    /// <b>not</b> the <c>all-options</c> of <c>BaseGetSelectOptionsEndpoint</c>. History uses the
    /// former.
    /// </para>
    /// </summary>
    [Fact]
    public async Task HistorySelectOptionsRoute_IsRegistered()
    {
        var response = await CreateUserRoleClient()
            .GetAsync("/api/activity-history/form-select-options", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// The seven dashboards are POSTs, so they need their own cases — a GET against them
    /// would 405, which is "not 404" for the wrong reason. Asserting on OK rather than "not 404"
    /// avoids that trap entirely. The dashboards sit two folders deep and are the likeliest routes to
    /// be lost in a folder-driven move, so all seven are listed.
    /// </summary>
    [Theory]
    [InlineData("/api/activity-history/dashboard/detail/pie-chart")]
    [InlineData("/api/activity-history/dashboard/detail/stacked-bars")]
    [InlineData("/api/activity-history/dashboard/detail/summary-cards")]
    [InlineData("/api/activity-history/dashboard/summary/pie-chart")]
    [InlineData("/api/activity-history/dashboard/summary/stacked-bars")]
    [InlineData("/api/activity-history/dashboard/summary/summary-cards")]
    [InlineData("/api/activity-history/dashboard/summary/time-of-day")]
    public async Task HistoryDashboardRoutes_AreRegistered(string route)
    {
        var response = await CreateUserRoleClient()
            .PostAsJsonAsync(route, DashboardBody(), JsonOpts, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed);
    }

    /// <summary>
    /// The per-activity aggregate the to-do calibration chip reads. Three properties matter to the
    /// caller and none of them are visible from the route alone: rows are summed <b>per activity id</b>
    /// (the pie-chart endpoint this replaces groups by non-unique activity <em>name</em>), an id with no
    /// logged history is <b>omitted</b> rather than returned as a zero — so the caller may divide by
    /// <c>entryCount</c> unguarded — and another user's rows are never counted.
    /// </summary>
    [Fact]
    public async Task AggregateByActivity_SumsPerId_OmitsEmptyIds_AndExcludesOtherUsers()
    {
        const long userId = FakeLoggedUserService.TestUserId;
        const long otherUserId = FakeLoggedUserService.TestUserId + 9_100;

        long loggedActivityId;
        long unloggedActivityId;
        long otherUsersActivityId;

        await using (var db = CreateDbContext())
        {
            loggedActivityId = await SeedActivityAsync(db, userId, "Aggregate logged");
            unloggedActivityId = await SeedActivityAsync(db, userId, "Aggregate unlogged");

            // The other user needs to exist for the FK; only the id matters.
            if (!await db.Set<User>().IgnoreQueryFilters().AnyAsync(u => u.Id == otherUserId, CancellationToken))
            {
                db.Set<User>().Add(new User
                {
                    Id = otherUserId,
                    Email = "aggregate-other@test.com",
                    NormalizedEmail = "AGGREGATE-OTHER@TEST.COM",
                    UserName = "aggregate-other@test.com",
                    NormalizedUserName = "AGGREGATE-OTHER@TEST.COM",
                    SecurityStamp = Guid.NewGuid().ToString("N"),
                    ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                    Locale = AvailableLocales.En,
                    Timezone = TimeZoneInfo.Utc
                });
                await db.SaveChangesAsync(CancellationToken);
            }

            otherUsersActivityId = await SeedActivityAsync(db, otherUserId, "Aggregate theirs");

            // Two rows on the one activity, months apart: the aggregate spans all of the user's history
            // with no date range, so both must land in the same total.
            db.Set<ActivityHistory>().AddRange(
                NewHistory(userId, loggedActivityId, DateTime.UtcNow.AddDays(-90)),
                NewHistory(userId, loggedActivityId, DateTime.UtcNow.AddHours(-3)),
                NewHistory(otherUserId, otherUsersActivityId, DateTime.UtcNow.AddHours(-4)));
            await db.SaveChangesAsync(CancellationToken);
        }

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            "/api/activity-history/aggregate-by-activity",
            new { activityIds = new[] { loggedActivityId, unloggedActivityId, otherUsersActivityId } },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var entries = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, TestContext.Current.CancellationToken))
            .EnumerateArray()
            .ToDictionary(e => e.GetProperty("activityId").GetInt64());

        entries.Should().ContainKey(loggedActivityId);
        entries[loggedActivityId].GetProperty("entryCount").GetInt32().Should().Be(2);
        // NewHistory logs 30 minutes per row.
        entries[loggedActivityId].GetProperty("totalSeconds").GetInt64().Should().Be(2 * 30 * 60);

        entries.Should().NotContainKey(unloggedActivityId,
            "an activity with no logged history is omitted, not returned with zeros");
        entries.Should().NotContainKey(otherUsersActivityId,
            "another user's logged history must never be aggregated into a response -- if this id is " +
            "present the endpoint's UserId predicate was dropped");
    }

    // ---- helpers ---------------------------------------------------------

    private static ActivityHistory NewHistory(long userId, long activityId, DateTime start) => new()
    {
        UserId = userId,
        ActivityId = activityId,
        StartTimestamp = start,
        EndTimestamp = start.AddMinutes(30),
        Length = new IntTime(0, 30)
    };

    private static async Task<long> SeedActivityAsync(DbContext db, long userId, string name)
    {
        var role = new ActivityRole { UserId = userId, Name = $"{name} Role", Color = "#112233" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity { UserId = userId, Name = name, RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);
        return activity.Id;
    }

    /// <summary>
    /// Superset body covering both dashboard request shapes: the <c>HistoryDetail*</c> ones bind
    /// <c>DateAndTimeRangeDto</c> (date + from/to times), the <c>HistorySummary*</c> ones bind
    /// <c>DateRangeDto</c> (date + rangeType). Extra members are ignored on either side, and since
    /// these cases only assert on routing, a 400 from a stricter validator is still a pass.
    /// </summary>
    private static object DashboardBody() => new
    {
        date = DateOnly.FromDateTime(DateTime.UtcNow),
        from = new { hours = 0, minutes = 0, seconds = 0 },
        to = new { hours = 23, minutes = 59, seconds = 0 },
        rangeType = "Week",
        groupBy = "Activity"
    };
}