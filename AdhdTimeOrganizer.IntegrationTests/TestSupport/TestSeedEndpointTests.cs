using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.application.dto.testSupport;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.testSupport;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.android;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.Testing;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Notifications.application;
using Sydowwe.Notifications.domain.entity;
using Sydowwe.Scheduler.domain.@enum;
using Sydowwe.Scheduler.domain.entity;
using Xunit;
using static AdhdTimeOrganizer.infrastructure.testSupport.E2eFixtureIds;

namespace AdhdTimeOrganizer.IntegrationTests.TestSupport;

/// <summary>
/// <c>POST /api/test/seed</c>, asserted on rows rather than on the route.
///
/// <para>Every claim this fixture makes to the Playwright suite is a claim about data — an item with a
/// <c>suggestedTime</c> whose activity has history, an overdue item, an archived activity, a day with
/// planner tasks next to a day without. A 200 says none of that. Each of the surfaces below was
/// unrenderable before this fixture existed precisely because the data never appeared, and a route-level
/// test would not have noticed.</para>
/// </summary>
[Collection("Postgres")]
public class TestSeedEndpointTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private HttpClient SeedClient() => Fixture.UnauthenticatedFactory.CreateClient();

    private AppDbContext Db() => (AppDbContext)CreateDbContext();

    private async Task<TestSeedResponse> SeedAsync()
    {
        using var client = SeedClient();
        var response = await client.PostAsync("/api/test/seed", null, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));

        var body = await response.Content.ReadFromJsonAsync<TestSeedResponse>(JsonOpts, CancellationToken);
        body.Should().NotBeNull();
        return body!;
    }

    // --- the contract ----------------------------------------------------------------------------

    [Fact]
    public async Task Seed_RunsWithoutAuthentication_AndReturnsTheStableIds()
    {
        var body = await SeedAsync();

        body.Fixture.Should().Be(FixtureVersion);
        body.PrimaryUser.Id.Should().Be(PrimaryUserId);
        body.PrimaryUser.Email.Should().Be(PrimaryUserEmail);
        body.SecondaryUser.Id.Should().Be(SecondaryUserId);
        body.Activities["bugFixing"].Should().Be(ActivityBugFixing);
        body.TodoLists["empty"].Should().Be(PrimaryListSomeday);
    }

    /// <summary>
    /// Twice must leave the same state as once — same rows, same ids, same values. This is the property
    /// the whole suite leans on: <c>globalSetup</c> runs on every local test run, against a database
    /// that already holds whatever the last run did to it.
    /// </summary>
    [Fact]
    public async Task Seed_Twice_LeavesIdenticalRows()
    {
        var firstResponse = await SeedAsync();
        var firstRows = await SnapshotAsync();

        var secondResponse = await SeedAsync();
        var secondRows = await SnapshotAsync();

        secondRows.Should().BeEquivalentTo(firstRows);
        secondResponse.Should().BeEquivalentTo(firstResponse);
    }

    /// <summary>
    /// Every value a spec could assert on, read straight out of the database. Deliberately not the
    /// response body: the body is what the endpoint claims, this is what is actually stored.
    /// </summary>
    private async Task<FixtureSnapshot> SnapshotAsync()
    {
        await using var db = Db();

        var items = await db.TodoListItems.IgnoreQueryFilters()
            .Where(item => item.UserId == PrimaryUserId)
            .OrderBy(item => item.Id)
            .Select(item => new
            {
                item.Id, item.ActivityId, item.TodoListId, item.IsDone, item.DoneCount, item.TotalCount,
                item.DueDate, item.DisplayOrder, item.PairedLeisureActivityId
            })
            .ToListAsync(CancellationToken);

        var activities = await db.Activities.IgnoreQueryFilters()
            .Where(activity => activity.UserId == PrimaryUserId)
            .OrderBy(activity => activity.Id)
            .Select(activity => new { activity.Id, activity.Name, activity.RoleId, activity.CategoryId, activity.IsArchived })
            .ToListAsync(CancellationToken);

        var history = await db.ActivityHistories.IgnoreQueryFilters()
            .Where(row => row.UserId == PrimaryUserId)
            .OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.ActivityId, row.StartTimestamp, row.EndTimestamp, row.TodoListItemId })
            .ToListAsync(CancellationToken);

        var plannerTasks = await db.PlannerTasks.IgnoreQueryFilters()
            .Where(task => task.UserId == PrimaryUserId)
            .OrderBy(task => task.Id)
            .Select(task => new { task.Id, task.ActivityId, task.CalendarId, task.StartTime, task.EndTime, task.Status })
            .ToListAsync(CancellationToken);

        var templates = await db.Set<TaskPlannerDayTemplate>().IgnoreQueryFilters()
            .Where(template => template.UserId == PrimaryUserId)
            .OrderBy(template => template.Id)
            .Select(template => new { template.Id, template.Name, template.IsPinned, template.SuggestedForDayType })
            .ToListAsync(CancellationToken);

        var templateTasks = await db.Set<TemplatePlannerTask>().IgnoreQueryFilters()
            .Where(task => task.UserId == PrimaryUserId)
            .OrderBy(task => task.Id)
            .Select(task => new { task.Id, task.TemplateId, task.ActivityId, task.StartTime, task.EndTime, task.ImportanceId })
            .ToListAsync(CancellationToken);

        // Notifications carry CreatedTimestamp in the snapshot on purpose: the bell orders on it, so it is a
        // value a spec can observe, and the seeder rewrites it from the seed date rather than the clock
        // precisely so that it survives this comparison.
        var notifications = await db.Set<Notification>()
            .Where(notification => notification.UserId == PrimaryUserId)
            .OrderBy(notification => notification.Id)
            .Select(notification => new { notification.Id, notification.Type, notification.IsRead, notification.CreatedTimestamp })
            .ToListAsync(CancellationToken);

        return new FixtureSnapshot(
            items.Select(item => item.ToString()!).ToList(),
            activities.Select(activity => activity.ToString()!).ToList(),
            history.Select(row => row.ToString()!).ToList(),
            plannerTasks.Select(task => task.ToString()!).ToList(),
            templates.Select(template => template.ToString()!).ToList(),
            templateTasks.Select(task => task.ToString()!).ToList(),
            notifications.Select(notification => notification.ToString()!).ToList(),
            await TrackingSnapshotAsync(db));
    }

    /// <summary>
    /// The tracking ledgers as an aggregate rather than row by row. They hold thousands of rows and no spec
    /// addresses one of them by id — what has to be stable across two resets is their shape: how many rows,
    /// over which window, and how many distinct groups the two settings grids will show.
    /// </summary>
    private async Task<string> TrackingSnapshotAsync(AppDbContext db)
    {
        var web = await db.Set<WebExtensionActivityEntry>().IgnoreQueryFilters()
            .Where(entry => entry.UserId == PrimaryUserId)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Count = group.Count(),
                First = group.Min(entry => entry.WindowStart),
                Last = group.Max(entry => entry.WindowStart),
                Seconds = group.Sum(entry => entry.ActiveSeconds)
            })
            .SingleAsync(CancellationToken);

        var desktop = await db.Set<DesktopActivityEntry>().IgnoreQueryFilters()
            .Where(entry => entry.UserId == PrimaryUserId)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Count = group.Count(),
                First = group.Min(entry => entry.WindowStart),
                Last = group.Max(entry => entry.WindowStart),
                Seconds = group.Sum(entry => entry.ActiveSeconds)
            })
            .SingleAsync(CancellationToken);

        var android = await db.Set<AndroidSessionData>().IgnoreQueryFilters()
            .Where(session => session.UserId == PrimaryUserId)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Count = group.Count(),
                First = group.Min(session => session.SessionStartUtc),
                Last = group.Max(session => session.SessionEndUtc),
                Seconds = group.Sum(session => session.DurationSeconds)
            })
            .SingleAsync(CancellationToken);

        return $"{web} {desktop} {android}";
    }

    private sealed record FixtureSnapshot(
        List<string> Items,
        List<string> Activities,
        List<string> History,
        List<string> PlannerTasks,
        List<string> Templates,
        List<string> TemplateTasks,
        List<string> Notifications,
        string Tracking);

    // --- fixture item 1: the e2e account itself --------------------------------------------------

    [Fact]
    public async Task Seed_CreatesBothAccounts_ConfirmedAndInEnglish()
    {
        await SeedAsync();

        await using var db = Db();
        var accounts = await db.Users
            .Where(user => user.Id == PrimaryUserId || user.Id == SecondaryUserId)
            .OrderBy(user => user.Id)
            .ToListAsync(CancellationToken);

        accounts.Should().HaveCount(2);
        accounts.Should().OnlyContain(user => user.EmailConfirmed);
        // The whole UI reads its locale off this column; a Slovak default breaks every label locator.
        accounts.Should().OnlyContain(user => user.Locale == AvailableLocales.En);
        accounts[0].Email.Should().Be(PrimaryUserEmail);
        accounts[1].Email.Should().Be(SecondaryUserEmail);
    }

    /// <summary>
    /// The credential in the SPA's <c>.env.test</c> does not satisfy this app's registration policy, so
    /// the seeder writes the hash directly. This is the check that the resulting account can still sign
    /// in — the thing that policy detour could plausibly have broken.
    /// </summary>
    [Fact]
    public async Task Seed_LeavesTheAccountAbleToSignInImmediately()
    {
        await SeedAsync();

        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Core.domain.model.entity.user.User>>();

        var account = await users.FindByEmailAsync(PrimaryUserEmail);
        account.Should().NotBeNull();
        (await users.CheckPasswordAsync(account!, PrimaryUserPassword)).Should().BeTrue();

        // And every account carries the User role, without which it authenticates and then 403s.
        (await users.GetRolesAsync(account!)).Should().Contain("User");
    }

    // --- fixture item 2: per-account isolation ---------------------------------------------------

    [Fact]
    public async Task Seed_GivesTheSecondAccountDataOfItsOwn()
    {
        await SeedAsync();

        await using var db = Db();

        var secondary = await db.TodoListItems.IgnoreQueryFilters()
            .Where(item => item.UserId == SecondaryUserId).ToListAsync(CancellationToken);
        secondary.Should().NotBeEmpty();

        // Nothing the second account owns may be reachable from the first account's ids.
        secondary.Should().OnlyContain(item => item.TodoListId == SecondaryListWork);
        (await db.Activities.IgnoreQueryFilters().CountAsync(a => a.UserId == SecondaryUserId, CancellationToken))
            .Should().BeGreaterThan(0);
    }

    // --- fixture item 3: estimate vs actual ------------------------------------------------------

    [Fact]
    public async Task Seed_LeavesAtLeastFiveItemsWithAnEstimateAndMatchingHistory()
    {
        var body = await SeedAsync();

        await using var db = Db();

        var withEstimate = await db.TodoListItems.IgnoreQueryFilters()
            .Where(item => item.UserId == PrimaryUserId && item.SuggestedTime != null)
            .Select(item => new { item.Id, item.ActivityId })
            .ToListAsync(CancellationToken);

        // "Non-zero", not merely "set" — an IntTime of 0h0m would satisfy a null check and render nothing.
        var suggested = await db.TodoListItems.IgnoreQueryFilters()
            .Where(item => item.UserId == PrimaryUserId && item.SuggestedTime != null)
            .Select(item => item.SuggestedTime!)
            .ToListAsync(CancellationToken);
        suggested.Should().OnlyContain(time => time.TotalSeconds > 0);

        var activityIdsWithHistory = await db.ActivityHistories.IgnoreQueryFilters()
            .Where(row => row.UserId == PrimaryUserId)
            .Select(row => row.ActivityId)
            .Distinct()
            .ToListAsync(CancellationToken);

        var comparable = withEstimate.Count(item => activityIdsWithHistory.Contains(item.ActivityId));

        comparable.Should().BeGreaterThanOrEqualTo(5,
            "the header ratio line is gated on at least five comparable items");
        body.Counts.CalibrationTodoListItems.Should().BeGreaterThanOrEqualTo(5);

        // Each calibration activity carries more than one recording, so an average is meaningful.
        var perActivity = await db.ActivityHistories.IgnoreQueryFilters()
            .Where(row => row.UserId == PrimaryUserId && row.ActivityId == ActivityBugFixing)
            .CountAsync(CancellationToken);
        perActivity.Should().BeGreaterThanOrEqualTo(3);

        // And at least one recording is attributed to the item itself, not just to the activity.
        (await db.ActivityHistories.IgnoreQueryFilters()
                .AnyAsync(row => row.TodoListItemId == ItemBugFixing, CancellationToken))
            .Should().BeTrue();
    }

    /// <summary>
    /// The rows above prove the fixture stored an estimate. This proves the client can <b>see</b> one.
    ///
    /// <para>They are not the same claim, and the gap between them was real: <c>SuggestedTime</c> sat on
    /// the entity and on both request DTOs but on no to-do response, so <c>GET /api/todo-list-item</c>
    /// returned no estimate field at all and the SPA's estimate-vs-actual chip and header calibration
    /// ratio had nothing to render. Every row assertion in this class passed throughout.</para>
    ///
    /// <para>So: any surface this fixture exists to make visible needs a guard on the projection, not
    /// only on the rows. This one goes through <c>TodoListItemResponse.Projection</c> — the same
    /// expression the endpoints use — rather than over HTTP, because the defect was in the projection
    /// and the fixture accounts are not the identity this suite authenticates as.</para>
    /// </summary>
    [Fact]
    public async Task Seed_LeavesTheEstimateVisibleThroughTheProjection()
    {
        await SeedAsync();

        await using var db = Db();

        var projected = await TodoListItemResponse.Projection(
                db.TodoListItems.IgnoreQueryFilters().Where(item => item.UserId == PrimaryUserId))
            .ToListAsync(CancellationToken);

        // Non-zero, not merely non-null: an IntTime of 0h0m survives a null check and renders nothing.
        projected.Where(item => item.SuggestedTime is not null)
            .Should().HaveCountGreaterThanOrEqualTo(5,
                "the calibration surfaces read SuggestedTime off the projection, so an estimate the "
                + "fixture stored but the response omits is invisible to every spec")
            .And.OnlyContain(item => item.SuggestedTime!.TotalSeconds > 0);

        projected.Single(item => item.Id == ItemBugFixing).SuggestedTime.Should().NotBeNull();
    }

    // --- fixture item 4: the overdue banner ------------------------------------------------------

    [Fact]
    public async Task Seed_LeavesSeveralOverdueNotDoneItems()
    {
        var body = await SeedAsync();
        var today = body.SeedDateUtc;

        await using var db = Db();

        var overdue = await db.TodoListItems.IgnoreQueryFilters()
            .Where(item => item.UserId == PrimaryUserId && !item.IsDone && item.DueDate != null && item.DueDate < today)
            .ToListAsync(CancellationToken);

        overdue.Should().HaveCountGreaterThanOrEqualTo(5,
            "the renegotiation banner's threshold lives in the SPA, so the fixture aims well above it");
        body.Counts.OverdueTodoListItems.Should().Be(overdue.Count);

        // Spread rather than clustered, so "1 day late" and "2 weeks late" both render.
        overdue.Select(item => item.DueDate).Distinct().Should().HaveCount(overdue.Count);
    }

    // --- fixture item 5: the multi-count counter -------------------------------------------------

    [Fact]
    public async Task Seed_LeavesAPartiallyCompleteMultiCountItem()
    {
        await SeedAsync();

        await using var db = Db();
        var item = await db.TodoListItems.IgnoreQueryFilters()
            .SingleAsync(row => row.Id == ItemDeepCleanKitchen, CancellationToken);

        item.TotalCount.Should().Be(5);
        item.DoneCount.Should().Be(2);
        item.IsDone.Should().BeFalse("a counter at 2 of 5 that is also marked done renders nothing useful");
    }

    // --- fixture item 6: planner range, populated and empty --------------------------------------

    [Fact]
    public async Task Seed_LeavesADayWithPlannerTasksAndADayWithout()
    {
        var body = await SeedAsync();
        var today = body.SeedDateUtc;

        await using var db = Db();

        var taskCountByDate = await db.PlannerTasks.IgnoreQueryFilters()
            .Where(task => task.UserId == PrimaryUserId)
            .GroupBy(task => task.Calendar.Date)
            .Select(group => new { Date = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Date, row => row.Count, CancellationToken);

        taskCountByDate.GetValueOrDefault(today).Should().BeGreaterThan(0, "/day-planner/today must have a populated state");
        taskCountByDate.GetValueOrDefault(today.AddDays(2)).Should().Be(0, "/day-planner/:date must have a reachable empty state");
        taskCountByDate.GetValueOrDefault(today.AddDays(3)).Should().BeGreaterThan(0, "the range must span the empty day");

        // The calendar days themselves exist for the whole range — the empty day is empty of tasks, not
        // missing, which is a different (and broken) state.
        (await db.Calendars.IgnoreQueryFilters()
                .CountAsync(calendar => calendar.UserId == PrimaryUserId && calendar.Date >= today && calendar.Date <= today.AddDays(3), CancellationToken))
            .Should().Be(4);
    }

    // --- fixture item 7: activity lifecycle ------------------------------------------------------

    [Fact]
    public async Task Seed_LeavesAnArchivedActivity_AndOneThatCannotBeDeleted()
    {
        await SeedAsync();

        await using var db = Db();

        var archived = await db.Activities.IgnoreQueryFilters()
            .SingleAsync(activity => activity.Id == ActivityRetiredHobby, CancellationToken);
        archived.IsArchived.Should().BeTrue();

        // ...and it is genuinely hidden by the archival query filter, not merely flagged.
        (await db.Activities.AnyAsync(activity => activity.Id == ActivityRetiredHobby, CancellationToken))
            .Should().BeFalse();

        // canDelete is summed across the IActivityReferenceSource implementations, so "referenced by at
        // least one row" is what the grid turns into canDelete = false.
        (await db.ActivityHistories.IgnoreQueryFilters()
                .AnyAsync(row => row.ActivityId == ActivityWriteBlogPost, CancellationToken))
            .Should().BeTrue();

        // The contrast case: referenced by nothing at all.
        (await db.ActivityHistories.IgnoreQueryFilters().AnyAsync(row => row.ActivityId == ActivityUnusedIdea, CancellationToken))
            .Should().BeFalse();
        (await db.TodoListItems.IgnoreQueryFilters().AnyAsync(item => item.ActivityId == ActivityUnusedIdea, CancellationToken))
            .Should().BeFalse();
        (await db.PlannerTasks.IgnoreQueryFilters().AnyAsync(task => task.ActivityId == ActivityUnusedIdea, CancellationToken))
            .Should().BeFalse();
    }

    // --- fixture item 8: the three leisure kinds and the anchor ----------------------------------

    [Fact]
    public async Task Seed_LeavesOneProfileOfEachKind_OnDistinctActivities_PlusAMemoryAnchor()
    {
        await SeedAsync();

        await using var db = Db();

        var backlog = await db.Set<ActivityBacklogProfile>().IgnoreQueryFilters().SingleAsync(CancellationToken);
        var bucketList = await db.Set<ActivityBucketListProfile>().IgnoreQueryFilters().SingleAsync(CancellationToken);
        var project = await db.Set<ActivityProjectProfile>().IgnoreQueryFilters().SingleAsync(CancellationToken);

        new[] { backlog.ActivityId, bucketList.ActivityId, project.ActivityId }
            .Should().OnlyHaveUniqueItems("the three kinds have to sit on distinct activities");

        var anchor = await db.Set<MemoryAnchor>().IgnoreQueryFilters().SingleAsync(CancellationToken);
        anchor.ActivityId.Should().Be(bucketList.ActivityId,
            "the anchor closes the loop on a one-time bucket-list entry");
        anchor.Rating.Should().BeInRange(1, 10);
    }

    // --- fixture item 9: day-planner templates ---------------------------------------------------

    /// <summary>
    /// Two of unit 08's three views are unreachable without a template — there is no id to navigate to, and
    /// the split view's two selectors have nothing to select. Two templates, not one, for the second reason:
    /// comparing a template against itself is not what that view is for.
    /// </summary>
    [Fact]
    public async Task Seed_LeavesAtLeastTwoDayPlannerTemplates_EachCarryingTasks()
    {
        var body = await SeedAsync();

        await using var db = Db();

        var templates = await db.Set<TaskPlannerDayTemplate>().IgnoreQueryFilters()
            .Where(template => template.UserId == PrimaryUserId)
            .ToListAsync(CancellationToken);

        templates.Should().HaveCountGreaterThanOrEqualTo(2,
            "/day-planner/templates/split puts one template beside another");
        templates.Should().OnlyContain(template => template.IsActive);
        templates.Should().Contain(template => template.IsPinned, "the list's pinned section is a surface too");
        body.Counts.DayPlannerTemplates.Should().Be(templates.Count);

        var tasksByTemplate = await db.Set<TemplatePlannerTask>().IgnoreQueryFilters()
            .Where(task => task.UserId == PrimaryUserId)
            .GroupBy(task => task.TemplateId)
            .Select(group => new { TemplateId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.TemplateId, row => row.Count, CancellationToken);

        // A template with no tasks renders as an empty detail view, which is the state this exists to avoid.
        templates.Should().OnlyContain(template => tasksByTemplate.ContainsKey(template.Id) && tasksByTemplate[template.Id] > 0);

        body.DayPlannerTemplates["homeOffice"].Should().Be(TemplateHomeOffice);
        body.DayPlannerTemplates["officeDay"].Should().Be(TemplateOfficeDay);
    }

    // --- fixture item 10: the three tracking ledgers ----------------------------------------------

    /// <summary>
    /// All four dashboards in unit 05 rendered their whole control surface over "No activity recorded for
    /// this period" because these three tables were empty. The range matters as much as the count: a
    /// dashboard defaulting to the last week over three days of data is still an empty state for most of it.
    /// </summary>
    [Fact]
    public async Task Seed_LeavesAllThreeTrackingLedgersPopulated_AcrossAWeek()
    {
        var body = await SeedAsync();
        var today = body.SeedDateUtc;

        await using var db = Db();

        var web = await db.Set<WebExtensionActivityEntry>().IgnoreQueryFilters()
            .Where(entry => entry.UserId == PrimaryUserId).ToListAsync(CancellationToken);
        var desktop = await db.Set<DesktopActivityEntry>().IgnoreQueryFilters()
            .Where(entry => entry.UserId == PrimaryUserId).ToListAsync(CancellationToken);
        var android = await db.Set<AndroidSessionData>().IgnoreQueryFilters()
            .Where(session => session.UserId == PrimaryUserId).ToListAsync(CancellationToken);

        web.Should().NotBeEmpty();
        desktop.Should().NotBeEmpty();
        android.Should().NotBeEmpty();

        body.Counts.WebExtensionEntries.Should().Be(web.Count);
        body.Counts.DesktopEntries.Should().Be(desktop.Count);
        body.Counts.AndroidSessions.Should().Be(android.Count);

        // Seven distinct days ending today, so every default range a dashboard offers lands on data.
        web.Select(entry => DateOnly.FromDateTime(entry.WindowStart)).Distinct()
            .Should().HaveCount(7).And.Contain(today);
        desktop.Select(entry => DateOnly.FromDateTime(entry.WindowStart)).Distinct()
            .Should().HaveCount(7).And.Contain(today);
        android.Select(session => DateOnly.FromDateTime(session.SessionStartUtc)).Distinct()
            .Should().HaveCount(7).And.Contain(today);

        // Minute-aligned, and load-bearing: WebExtensionTimelineEndpoint derives each window's end as
        // WindowStart.AddMinutes(1) and stitches windows whose start equals the previous end, so an
        // unaligned value silently produces a gapped timeline rather than an error.
        web.Should().OnlyContain(entry => entry.WindowStart.Second == 0 && entry.WindowStart.Millisecond == 0);
        desktop.Should().OnlyContain(entry => entry.WindowStart.Second == 0 && entry.WindowStart.Millisecond == 0);

        // And the seconds are actually there — a ledger of zero-second windows is an empty chart with rows.
        web.Sum(entry => entry.ActiveSeconds).Should().BeGreaterThan(0);
        desktop.Sum(entry => entry.ActiveSeconds).Should().BeGreaterThan(0);
        android.Should().OnlyContain(session => session.DurationSeconds > 0);

        // The phone's stated duration has to agree with its own instants, or AndroidSyncEndpoint would have
        // dropped the session — a fixture row the real path would refuse is not a fixture.
        android.Should().OnlyContain(session =>
            Math.Abs((long)(session.SessionEndUtc - session.SessionStartUtc).TotalSeconds - session.DurationSeconds) <= 2);
    }

    /// <summary>
    /// The one rule the desktop ingest path applies that a row insert would not: a mapped process has its
    /// active seconds attributed to an activity, and an ignored one never reaches the ledger at all. Both are
    /// asserted here because the fixture claims to describe a state that pipeline could have produced.
    /// </summary>
    [Fact]
    public async Task Seed_AttributesTheMappedDesktopProcess_AndStoresNothingForTheIgnoredOne()
    {
        await SeedAsync();

        await using var db = Db();

        var mapping = await db.Set<TrackerDesktopMappingByPattern>().IgnoreQueryFilters()
            .SingleAsync(row => row.Id == DesktopMappingRider, CancellationToken);
        mapping.ActivityId.Should().Be(ActivityDeskResearch);
        mapping.IsActive.Should().BeTrue();

        var mappedEntries = await db.Set<DesktopActivityEntry>().IgnoreQueryFilters()
            .Where(entry => entry.UserId == PrimaryUserId && entry.ProcessName == mapping.ProcessName)
            .ToListAsync(CancellationToken);
        mappedEntries.Should().NotBeEmpty();

        // One ActivityHistory row per contiguous block, written through IActivityTimeAttributionSink — not
        // one per window, which is what a ledger of sub-minute windows would have fragmented into.
        var attributed = await db.ActivityHistories.IgnoreQueryFilters()
            .Where(row => row.UserId == PrimaryUserId && row.ActivityId == ActivityDeskResearch)
            .ToListAsync(CancellationToken);

        attributed.Should().HaveCount(7, "one block a day, over the same week the ledger covers");
        attributed.Sum(row => row.Length.TotalSeconds)
            .Should().Be(mappedEntries.Sum(entry => entry.ActiveSeconds),
                "the attributed total is the seconds the ledger recorded, not an approximation of them");

        // The ignore rule's process is absent, which is what the heartbeat dropping it looks like afterwards.
        var ignored = await db.Set<TrackerDesktopMappingByPattern>().IgnoreQueryFilters()
            .SingleAsync(row => row.Id == DesktopMappingIgnoredLauncher, CancellationToken);
        ignored.IsIgnored.Should().BeTrue();
        (await db.Set<DesktopActivityEntry>().IgnoreQueryFilters()
                .AnyAsync(entry => entry.ProcessName == ignored.ProcessName, CancellationToken))
            .Should().BeFalse("an entry an ignore rule matches never reaches the ledger");
    }

    /// <summary>
    /// Unit 06's two views are a filter row and an action bar over a table. Both need unmapped rows to act on
    /// and a non-empty <i>Mappings</i> tab beside them, and the distinct grids group rather than list — so
    /// what has to be non-trivial is the number of <i>groups</i>, not the number of entries.
    /// </summary>
    [Fact]
    public async Task Seed_LeavesDistinctTrackingEntriesToMap_AndMappingsBesideThem()
    {
        var body = await SeedAsync();

        await using var db = Db();

        // The same GROUP BY FetchTableDistinctDesktopEntry runs.
        var distinctDesktop = await db.Set<DesktopActivityEntry>().IgnoreQueryFilters()
            .Where(entry => entry.UserId == PrimaryUserId)
            .GroupBy(entry => new { entry.ProductName, entry.ProcessName, entry.WindowTitle })
            .Select(group => group.Key)
            .ToListAsync(CancellationToken);

        distinctDesktop.Should().HaveCountGreaterThanOrEqualTo(4,
            "a grid with one row per process cannot demonstrate filtering, sorting or a batch mapping");
        body.Counts.DistinctDesktopEntries.Should().Be(distinctDesktop.Count);

        // More groups than processes: the window title is part of the key, and a single title per process
        // would make that column dead weight on both the grid and its filter row.
        distinctDesktop.Select(key => key.ProcessName).Distinct().Count()
            .Should().BeLessThan(distinctDesktop.Count);

        var distinctAndroid = await db.Set<AndroidSessionData>().IgnoreQueryFilters()
            .Where(session => session.UserId == PrimaryUserId)
            .GroupBy(session => new { session.PackageName, session.AppLabel })
            .Select(group => group.Key)
            .ToListAsync(CancellationToken);

        distinctAndroid.Should().HaveCountGreaterThanOrEqualTo(4);
        body.Counts.DistinctAndroidEntries.Should().Be(distinctAndroid.Count);

        // The Mappings tab beside each grid, with both outcomes represented.
        var desktopMappings = await db.Set<TrackerDesktopMappingByPattern>().IgnoreQueryFilters()
            .Where(row => row.UserId == PrimaryUserId).ToListAsync(CancellationToken);
        desktopMappings.Should().Contain(row => row.ActivityId != null)
            .And.Contain(row => row.IsIgnored == true);

        var androidMappings = await db.Set<TrackerAndroidMappingByPattern>().IgnoreQueryFilters()
            .Where(row => row.UserId == PrimaryUserId).ToListAsync(CancellationToken);
        androidMappings.Should().Contain(row => row.ActivityId != null)
            .And.Contain(row => row.IsIgnored == true);

        // Unmapped rows remain, or there is nothing left for the mapping workflow to act on.
        var mappedProcesses = desktopMappings.Select(row => row.ProcessName).ToHashSet();
        distinctDesktop.Should().Contain(key => !mappedProcesses.Contains(key.ProcessName));
    }

    // --- fixture item 11: the notification bell ---------------------------------------------------

    /// <summary>
    /// <c>/notifikacie</c> rendered "No notifications yet" while its sibling view in the same unit was fully
    /// populated. The bell's two filters need rows that differ: unread against read, and more than one type.
    /// </summary>
    [Fact]
    public async Task Seed_LeavesNotificationsInBothReadStates_AcrossSeveralTypes()
    {
        var body = await SeedAsync();

        await using var db = Db();

        var notifications = await db.Set<Notification>()
            .Where(notification => notification.UserId == PrimaryUserId)
            .ToListAsync(CancellationToken);

        notifications.Should().HaveCountGreaterThanOrEqualTo(5);
        body.Counts.Notifications.Should().Be(notifications.Count);

        var unread = notifications.Count(notification => !notification.IsRead);
        unread.Should().BeGreaterThan(0);
        unread.Should().BeLessThan(notifications.Count, "Unread has to return a strictly smaller set than All");
        body.Counts.UnreadNotifications.Should().Be(unread);

        notifications.Select(notification => notification.Type).Distinct()
            .Should().HaveCountGreaterThanOrEqualTo(3, "the type filter needs something to separate");

        // ReadAt tracks IsRead in both directions — an un-read row carrying a stamp is a third state nothing
        // renders, and a read row without one loses the only instant the retention job could key on.
        notifications.Where(notification => notification.IsRead)
            .Should().OnlyContain(notification => notification.ReadAt != null);
        notifications.Where(notification => !notification.IsRead)
            .Should().OnlyContain(notification => notification.ReadAt == null);

        // Distinct creation instants, because the bell sorts on them and ties sort arbitrarily.
        notifications.Select(notification => notification.CreatedTimestamp).Distinct()
            .Should().HaveCount(notifications.Count);

        // Per-account, not global: the second account's bell is its own.
        (await db.Set<Notification>().CountAsync(notification => notification.UserId == SecondaryUserId, CancellationToken))
            .Should().BeGreaterThan(0);
    }

    /// <summary>
    /// The rows above prove the bell has content. This proves it renders as content: every kind's body is
    /// produced by <c>NotificationTextRenderer</c> from the type plus the payload, and a payload it cannot
    /// bind falls through to a generic "Máte novú notifikáciu" rather than throwing — so a drifted payload
    /// seeds rows that look like a renderer bug and pass every row assertion in this class.
    /// </summary>
    [Fact]
    public async Task Seed_LeavesEveryNotificationRenderingItsOwnText()
    {
        await SeedAsync();

        await using var db = Db();

        var renderer = Fixture.UnauthenticatedFactory.Services.GetRequiredService<INotificationTextRenderer>();

        var notifications = await db.Set<Notification>()
            .Where(notification => notification.UserId == PrimaryUserId)
            .ToListAsync(CancellationToken);

        foreach (var notification in notifications)
        {
            var (title, bodyText) = renderer.Render(notification.Type, notification.PayloadJson);

            title.Should().NotBeNullOrWhiteSpace();
            bodyText.Should().NotBe("Máte novú notifikáciu.",
                $"the payload seeded for {notification.Type} has to bind to the record that type declares");
        }

        // And the one kind whose payload carries a concrete value renders that value rather than its fallback.
        var digest = notifications.Single(notification => notification.Type == NotificationType.ReminderDigest);
        renderer.Render(digest.Type, digest.PayloadJson).Body.Should().Contain("3");
    }

    // --- scope ------------------------------------------------------------------------------------

    /// <summary>
    /// The reset is scoped: it clears what the tests touch and leaves migration state and the scheduler's
    /// operational tables alone. The scheduler exclusion is easy to lose in a refactor and would cost the
    /// app every recurring job until its next restart, silently.
    /// </summary>
    [Fact]
    public async Task Seed_LeavesMigrationHistoryAndTheSchedulerAlone()
    {
        // Respawn empties every table between tests and the registrars only run at boot, so the row has
        // to be put back by hand — the point is what the seeder does to it, not where it came from.
        await using (var before = Db())
        {
            before.ScheduledJobs.Add(new ScheduledJob
            {
                JobKey = "e2e-scheduler-survival-probe",
                HandlerKey = "e2e.probe",
                OwnerModule = "IntegrationTests",
                ScheduleType = JobScheduleType.Cron,
                Cron = "0 0 * * * ?",
                Status = JobStatus.Active,
                IsActive = true,
                MaxRetries = 0
            });
            await before.SaveChangesAsync(CancellationToken);
        }

        await SeedAsync();

        await using var db = Db();
        (await db.ScheduledJobs.AnyAsync(job => job.JobKey == "e2e-scheduler-survival-probe", CancellationToken))
            .Should().BeTrue("the scheduler's tables are operational state, not fixture data");

        // EnsureCreated builds this schema without a migration history table, so its absence from the
        // database is not evidence. What matters is that the seeder cannot name it: the truncate list is
        // derived from the model, and __EFMigrationsHistory is not an entity in it.
        db.Model.GetEntityTypes().Select(type => type.GetTableName())
            .Should().NotContain("__EFMigrationsHistory");
    }

    /// <summary>
    /// After a reset the sequences sit above the fixture band, so the first row the app creates during a
    /// spec cannot be handed an id the fixture already used.
    /// </summary>
    [Fact]
    public async Task Seed_LeavesSequencesAboveTheFixtureBand()
    {
        await SeedAsync();

        await using var db = Db();

        var activity = new Activity
        {
            UserId = PrimaryUserId,
            Name = "Created by a spec",
            RoleId = PrimaryRoleWork,
            IsUnavoidable = false
        };
        db.Add(activity);
        await db.SaveChangesAsync(CancellationToken);

        activity.Id.Should().BeGreaterThan(ActivityTakeMedication);
    }
}
