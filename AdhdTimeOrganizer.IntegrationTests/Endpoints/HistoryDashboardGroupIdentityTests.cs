using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// Pins the thing that identifies a dashboard group: <b>the id of the entity it was grouped by</b>, carried
/// on every group-shaped response as <c>groupId</c> (<c>roleId</c> on the calendar's top roles), alongside
/// the <c>name</c> that is still the only thing rendered.
///
/// <para>The endpoints used to group by <c>(Name, Color)</c> and hand the client nothing but the name. That
/// does not collide today only because <c>Activity</c>, <c>ActivityRole</c> and <c>ActivityCategory</c> each
/// carry an unfiltered unique index on <c>(UserId, Name)</c> and every dashboard is user-scoped — a
/// constraint these endpoints neither state nor can see. What is broken without an id is identity over time
/// and identity against the synthetic buckets, which is what the cases below assert.</para>
///
/// <para>Every assertion is on <b>which groups come back and which ids they carry</b>, never on a status
/// code: a name-keyed response is well-formed and its numbers add up, so nothing here fails loudly.</para>
/// </summary>
[Collection("Postgres")]
public class HistoryDashboardGroupIdentityTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    /// <summary>The anchor date every request below is built around; the user is on UTC, as the fixture seeds them.</summary>
    private static readonly DateOnly Anchor = new(2026, 7, 15);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The core of the ask. A rename changes what a group is <i>called</i> and nothing about what it
    /// <i>is</i> — so <c>groupId</c> must be the same value before and after, while <c>name</c> follows the
    /// rename. Keyed by name, these two responses describe one group vanishing and another appearing.
    /// </summary>
    [Fact]
    public async Task PieChart_GroupIdSurvivesARename_WhileTheNameFollowsIt()
    {
        var (activityId, _) = await SeedActivityAsync("Reading");
        await SeedHistoryAsync((activityId, new DateTime(2026, 7, 14, 8, 0, 0, DateTimeKind.Utc)));

        var before = await ItemsAsync("/api/activity-history/dashboard/summary/pie-chart", ThreeDayRequest("Activity"), "items");
        before.Should().ContainSingle().Which.GetProperty("groupId").GetInt64().Should().Be(activityId,
            "the slice is the activity it aggregates, and its id is what says so");

        await RenameActivityAsync(activityId, "Reading (evenings)");

        var after = (await ItemsAsync("/api/activity-history/dashboard/summary/pie-chart", ThreeDayRequest("Activity"), "items"))
            .Should().ContainSingle().Subject;

        after.GetProperty("groupId").GetInt64().Should().Be(activityId,
            "the same activity, the same rows, the same group -- an id that moves when the label moves is " +
            "not an id, and a client that kept the earlier response reads the rename as one group " +
            "disappearing and a new one taking its place");
        after.GetProperty("name").GetString().Should().Be("Reading (evenings)",
            "name is still what renders, and it must track the rename -- the id is added beside it, not " +
            "in place of it");
    }

    /// <summary>
    /// <c>groupBy</c> decides which entity the id names. All three values are exercised against one seed, so a
    /// resolver that returns the activity's id for every mode cannot pass.
    /// </summary>
    [Fact]
    public async Task PieChart_GroupIdNamesTheEntityThatGroupByAsksFor()
    {
        var categoryId = await SeedCategoryAsync("Study");
        var (activityId, roleId) = await SeedActivityAsync("Reading", categoryId);

        await SeedHistoryAsync((activityId, new DateTime(2026, 7, 14, 8, 0, 0, DateTimeKind.Utc)));

        (await SingleGroupIdAsync("Activity")).Should().Be(activityId);
        (await SingleGroupIdAsync("Role")).Should().Be(roleId);
        (await SingleGroupIdAsync("Category")).Should().Be(categoryId);
        return;

        async Task<long> SingleGroupIdAsync(string groupBy)
        {
            var items = await ItemsAsync("/api/activity-history/dashboard/summary/pie-chart", ThreeDayRequest(groupBy), "items");
            return items.Should().ContainSingle().Subject.GetProperty("groupId").GetInt64();
        }
    }

    /// <summary>The detail family resolves groups through the same helper; this is the check that it does.</summary>
    [Fact]
    public async Task DetailPieChart_CarriesTheGroupIdToo()
    {
        var (activityId, _) = await SeedActivityAsync("Reading");
        await SeedHistoryAsync((activityId, new DateTime(2026, 7, 15, 8, 0, 0, DateTimeKind.Utc)));

        var items = await ItemsAsync("/api/activity-history/dashboard/detail/pie-chart", new
        {
            date = Anchor,
            from = new { hours = 0, minutes = 0 },
            to = new { hours = 23, minutes = 59 },
            groupBy = "Activity",
            maxItems = 20
        }, "items");

        items.Should().ContainSingle().Which.GetProperty("groupId").GetInt64().Should().Be(activityId);
    }

    /// <summary>The stacked bars' segments carry it as well. A whole-day window puts the seeded row in one bar.</summary>
    [Fact]
    public async Task StackedBars_SegmentsCarryTheGroupId()
    {
        var (activityId, roleId) = await SeedActivityAsync("Reading");
        await SeedHistoryAsync((activityId, new DateTime(2026, 7, 14, 8, 0, 0, DateTimeKind.Utc)));

        var response = await PostAsync("/api/activity-history/dashboard/summary/stacked-bars", new
        {
            date = Anchor,
            rangeType = "ThreeDays",
            groupBy = "Role",
            windowMinutes = 1440,
            windowStartTime = new { hours = 0, minutes = 0 },
            windowEndTime = new { hours = 0, minutes = 0 }
        });

        var segments = response.GetProperty("windows").EnumerateArray()
            .SelectMany(w => w.GetProperty("items").EnumerateArray())
            .ToList();

        segments.Should().ContainSingle().Which.GetProperty("groupId").GetInt64().Should().Be(roleId,
            "a bar segment is a group like any other and the frontend cross-highlights it against the pie " +
            "and the cards, so it needs the same key they carry");
    }

    /// <summary>
    /// The summary cards carry the id, and the period comparison behind <c>percentChange</c> / <c>isNew</c>
    /// is resolved against the same identity: an activity with time in the baseline week is not new, and one
    /// renamed since is still not new.
    /// </summary>
    [Fact]
    public async Task SummaryCards_CarryTheGroupId_AndCompareAgainstThatSameGroupsPast()
    {
        var (activityId, _) = await SeedActivityAsync("Reading");

        // ThreeDays over 2026-07-15 covers 07-12..07-15; the last7days baseline is the week before it.
        await SeedHistoryAsync(
            (activityId, new DateTime(2026, 7, 14, 8, 0, 0, DateTimeKind.Utc)),
            (activityId, new DateTime(2026, 7, 8, 8, 0, 0, DateTimeKind.Utc)));

        await RenameActivityAsync(activityId, "Reading (evenings)");

        var card = (await ItemsAsync("/api/activity-history/dashboard/summary/summary-cards", new
        {
            date = Anchor,
            rangeType = "ThreeDays",
            groupBy = "Activity",
            baseline = "last7days",
            topN = 4
        }, "cards")).Should().ContainSingle().Subject;

        card.GetProperty("groupId").GetInt64().Should().Be(activityId);
        card.GetProperty("isNew").GetBoolean().Should().BeFalse(
            "this activity has half an hour in the baseline week -- reporting it as new would mean the " +
            "baseline was matched on something other than the group itself");
    }

    /// <summary>
    /// The one group that is not an entity. Activities with no category roll into a synthetic
    /// <c>Uncategorized</c> bucket, and that bucket gets a null id — the signal that the frontend must fall
    /// back to keying it by name. A categorised activity alongside it carries the category's real id.
    /// </summary>
    [Fact]
    public async Task PieChart_GroupedByCategory_EmitsNullGroupIdOnlyForTheUncategorizedBucket()
    {
        var categoryId = await SeedCategoryAsync("Study");

        var (uncategorizedId, _) = await SeedActivityAsync("No category here");
        var (categorizedId, _) = await SeedActivityAsync("Categorised", categoryId);

        await SeedHistoryAsync(
            (uncategorizedId, new DateTime(2026, 7, 14, 8, 0, 0, DateTimeKind.Utc)),
            (categorizedId, new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc)));

        var items = await ItemsAsync("/api/activity-history/dashboard/summary/pie-chart", ThreeDayRequest("Category"), "items");

        var uncategorized = items.Should().ContainSingle(i => i.GetProperty("name").GetString() == "Uncategorized").Subject;
        uncategorized.GetProperty("groupId").ValueKind.Should().Be(JsonValueKind.Null,
            "there is no category row behind this bucket, so there is no id to give -- the frontend keys it " +
            "by name and must be told so by a null rather than by a fabricated id");

        var categorised = items.Should().ContainSingle(i => i.GetProperty("name").GetString() == "Study").Subject;
        categorised.GetProperty("groupId").GetInt64().Should().Be(categoryId);
    }

    /// <summary>
    /// The calendar dashboard's per-day role rows. A role is always present on an activity, so this id is
    /// never null.
    /// </summary>
    [Fact]
    public async Task Calendar_TopRolesCarryTheRoleId()
    {
        var (activityId, roleId) = await SeedActivityAsync("Whatever");
        await SeedCalendarDayAsync(new DateOnly(2026, 7, 14));
        await SeedHistoryAsync((activityId, new DateTime(2026, 7, 14, 8, 0, 0, DateTimeKind.Utc)));

        var response = await PostAsync("/api/activity-history/dashboard/calendar", new
        {
            startDate = new DateOnly(2026, 7, 14),
            endDate = new DateOnly(2026, 7, 14),
            topN = 3
        });

        var roles = response.EnumerateArray()
            .SelectMany(d => d.GetProperty("topRoles").EnumerateArray())
            .ToList();

        roles.Should().ContainSingle().Which
            .GetProperty("roleId").GetInt64().Should().Be(roleId,
                "the day's role rows are identified by the role they aggregate, not by its name");
    }

    // ---- helpers ---------------------------------------------------------

    private static object ThreeDayRequest(string groupBy) => new
    {
        date = Anchor,
        rangeType = "ThreeDays",
        groupBy,
        maxItems = 20
    };

    /// <summary>
    /// Each activity gets its own role, so grouping by activity and grouping by role are equally
    /// discriminating. Names are unique per user (an unfiltered unique index on every one of the three
    /// grouped-by entities), hence the <see cref="Guid"/> on the role's.
    /// </summary>
    private async Task<(long ActivityId, long RoleId)> SeedActivityAsync(string name, long? categoryId = null)
    {
        await using var db = CreateDbContext();

        var role = new ActivityRole { UserId = UserId, Name = $"role-{Guid.NewGuid():N}", Color = "#334455" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity { UserId = UserId, Name = name, RoleId = role.Id, CategoryId = categoryId };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);

        return (activity.Id, role.Id);
    }

    private async Task RenameActivityAsync(long activityId, string newName)
    {
        await using var db = CreateDbContext();

        var activity = await db.Set<Activity>().IgnoreQueryFilters().SingleAsync(a => a.Id == activityId, CancellationToken);
        activity.Name = newName;
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task<long> SeedCategoryAsync(string name)
    {
        await using var db = CreateDbContext();

        var category = new ActivityCategory { UserId = UserId, Name = name, Color = "#665544" };
        db.Set<ActivityCategory>().Add(category);
        await db.SaveChangesAsync(CancellationToken);

        return category.Id;
    }

    private async Task SeedCalendarDayAsync(DateOnly date)
    {
        await using var db = CreateDbContext();
        await PlanningTestSeedHelper.SeedCalendarAsync(db, date, UserId, CancellationToken);
    }

    /// <summary>Half-hour rows, so each seeded row contributes exactly 1800 seconds.</summary>
    private async Task SeedHistoryAsync(params (long ActivityId, DateTime StartUtc)[] rows)
    {
        await using var db = CreateDbContext();

        db.Set<ActivityHistory>().AddRange(rows.Select(r => new ActivityHistory
        {
            UserId = UserId,
            ActivityId = r.ActivityId,
            StartTimestamp = r.StartUtc,
            EndTimestamp = r.StartUtc.AddMinutes(30),
            Length = new IntTime(0, 30)
        }));

        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task<JsonElement> PostAsync(string route, object body)
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(route, body, Json, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, CancellationToken);
    }

    private async Task<List<JsonElement>> ItemsAsync(string route, object body, string arrayProperty)
    {
        return (await PostAsync(route, body)).GetProperty(arrayProperty).EnumerateArray().ToList();
    }
}
