using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The History counterpart of <see cref="CoreRouteSmokeTests"/> / <c>TodoListsRouteSmokeTests</c>,
/// plus the one behavioural check the History extraction actually needed.
/// <para>
/// Two traps are covered. The first is shared with every slice: the endpoints only route if
/// <c>AdhdTimeOrganizer.History</c> is in the FastEndpoints <c>o.Assemblies</c> list in
/// <c>Program.cs</c> (<c>DisableAutoDiscovery = true</c>), and leaving it out is not a build error —
/// every history route simply 404s.
/// </para>
/// <para>
/// The second is specific to this slice. History's grid used to filter to-do / routine membership by
/// querying <c>TodoListItems</c> / <c>RoutineTodoLists</c> directly; it now goes through Core's
/// <c>IActivityMembershipSource</c> seam, which is what keeps History's only project reference pointed
/// at Core. That indirection is resolved from DI by string key, so a source that fails to register —
/// or registers under the wrong key — does not fail the build and does not throw: the filter silently
/// stops narrowing and the grid returns rows it should have excluded. Only an assertion on the rows
/// catches it, which is what <see cref="Grid_MembershipFilter_NarrowsThroughTheSeam"/> is for.
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
    /// The grid and the six dashboards are POSTs, so they need their own cases — a GET against them
    /// would 405, which is "not 404" for the wrong reason. Asserting on OK rather than "not 404"
    /// avoids that trap entirely. The dashboards sit two folders deep and are the likeliest routes to
    /// be lost in a folder-driven move, so all six are listed.
    /// </summary>
    [Theory]
    [InlineData("/api/activity-history/dashboard/detail/pie-chart")]
    [InlineData("/api/activity-history/dashboard/detail/stacked-bars")]
    [InlineData("/api/activity-history/dashboard/detail/summary-cards")]
    [InlineData("/api/activity-history/dashboard/summary/pie-chart")]
    [InlineData("/api/activity-history/dashboard/summary/stacked-bars")]
    [InlineData("/api/activity-history/dashboard/summary/summary-cards")]
    public async Task HistoryDashboardRoutes_AreRegistered(string route)
    {
        var response = await CreateUserRoleClient()
            .PostAsJsonAsync(route, DashboardBody(), JsonOpts, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task HistoryGridRoute_IsRegistered()
    {
        var response = await CreateUserRoleClient()
            .PostAsJsonAsync("/api/activity-history/gird", GridBody(null), JsonOpts, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Seeds two activities for one user — one carried by a to-do list item, one not — with a history
    /// row each, then drives <c>IsFromTodoList</c> both ways. If the seam is wired the two calls
    /// partition the rows; if the source failed to register, both calls return both rows.
    /// </summary>
    [Fact]
    public async Task Grid_MembershipFilter_NarrowsThroughTheSeam()
    {
        long onListActivityId;
        long offListActivityId;

        await using (var db = CreateDbContext())
        {
            // The same id RoleTestAuthHandler signs requests with, so these rows survive the global
            // IEntityWithUser query filter the grid reads through.
            const long userId = FakeLoggedUserService.TestUserId;

            onListActivityId = await SeedActivityAsync(db, userId, "On a to-do list");
            offListActivityId = await SeedActivityAsync(db, userId, "Not on any list");

            var priority = new TaskPriority { UserId = userId, Text = "Normal", Color = "#445566", Priority = 1 };
            db.Set<TaskPriority>().Add(priority);
            await db.SaveChangesAsync(CancellationToken);

            db.Set<TodoListItem>().Add(new TodoListItem
            {
                UserId = userId,
                ActivityId = onListActivityId,
                TaskPriorityId = priority.Id
            });

            db.Set<ActivityHistory>().AddRange(
                NewHistory(userId, onListActivityId, DateTime.UtcNow.AddHours(-2)),
                NewHistory(userId, offListActivityId, DateTime.UtcNow.AddHours(-1)));
            await db.SaveChangesAsync(CancellationToken);
        }

        var onList = await GridActivityIdsAsync(isFromTodoList: true);
        var offList = await GridActivityIdsAsync(isFromTodoList: false);

        onList.Should().Contain(onListActivityId)
            .And.NotContain(offListActivityId,
                "IsFromTodoList=true must exclude activities absent from every to-do list");
        offList.Should().Contain(offListActivityId)
            .And.NotContain(onListActivityId,
                "IsFromTodoList=false must exclude activities present on a to-do list -- if both " +
                "assertions' sets are identical, IActivityMembershipSource did not resolve and the " +
                "filter degraded to a no-op");
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

    private async Task<List<long>> GridActivityIdsAsync(bool isFromTodoList)
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(
            "/api/activity-history/gird", GridBody(isFromTodoList), JsonOpts, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var groups = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, TestContext.Current.CancellationToken);
        return groups.EnumerateArray()
            .SelectMany(g => g.GetProperty("historyResponseList").EnumerateArray())
            .Select(h => h.GetProperty("activity").GetProperty("id").GetInt64())
            .ToList();
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

    private static object GridBody(bool? isFromTodoList) => new
    {
        useFilter = isFromTodoList.HasValue,
        filter = new { isFromTodoList },
        sortBy = Array.Empty<object>(),
        itemsPerPage = 100,
        page = 1
    };
}
