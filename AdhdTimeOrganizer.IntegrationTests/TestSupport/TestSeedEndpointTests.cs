using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.application.dto.testSupport;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.infrastructure.security;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.testSupport;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.application.service.reminder;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.entity.reminder;
using AdhdTimeOrganizer.Routines.application.service.routine;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
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
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.Testing;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Notifications.application;
using Sydowwe.Notifications.domain.entity;
using Sydowwe.Reminders.application.dto.reminderDefinition;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Reminders.domain.@enum;
using Sydowwe.Framework.Contracts.reminders;
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

        var routines = await db.RoutineTodoLists.IgnoreQueryFilters()
            .Where(routine => routine.UserId == PrimaryUserId)
            .OrderBy(routine => routine.Id)
            .Select(routine => new
            {
                routine.Id, routine.ActivityId, routine.TimePeriodId, routine.IsDone, routine.Streak, routine.BestStreak,
                routine.LastResetDate, routine.LastCompletedAt, routine.DisplayOrder
            })
            .ToListAsync(CancellationToken);

        // The period-level state the history seeder writes onto the per-user default rows, and the ledger
        // beneath it. The completion ids are identity-generated and excluded on purpose: nothing addresses one.
        var periods = await db.RoutineTimePeriods.IgnoreQueryFilters()
            .Where(period => period.UserId == PrimaryUserId)
            .OrderBy(period => period.Id)
            .Select(period => new
            {
                period.Id, period.Text, period.Streak, period.BestStreak, period.LastResetAt,
                period.FreezesRemaining, period.FreezeBudgetResetsAt
            })
            .ToListAsync(CancellationToken);

        var completions = await db.Set<RoutinePeriodCompletion>()
            .Where(completion => completion.RoutineTimePeriod.UserId == PrimaryUserId)
            .OrderBy(completion => completion.TimePeriodId).ThenBy(completion => completion.PeriodStart)
            .Select(completion => new
            {
                completion.TimePeriodId, completion.PeriodStart, completion.PeriodEnd, completion.CompletedCount,
                completion.TotalCount, completion.IsFrozen, completion.FrozenAt, completion.CreatedAt
            })
            .ToListAsync(CancellationToken);

        return new FixtureSnapshot(
            items.Select(item => item.ToString()!).ToList(),
            activities.Select(activity => activity.ToString()!).ToList(),
            history.Select(row => row.ToString()!).ToList(),
            plannerTasks.Select(task => task.ToString()!).ToList(),
            templates.Select(template => template.ToString()!).ToList(),
            templateTasks.Select(task => task.ToString()!).ToList(),
            notifications.Select(notification => notification.ToString()!).ToList(),
            routines.Select(routine => routine.ToString()!).ToList(),
            periods.Select(period => period.ToString()!).ToList(),
            completions.Select(completion => completion.ToString()!).ToList(),
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
        List<string> Routines,
        List<string> RoutinePeriods,
        List<string> RoutineCompletions,
        string Tracking);

    // --- fixture item 1: the e2e account itself --------------------------------------------------

    [Fact]
    public async Task Seed_CreatesAllThreeAccounts_ConfirmedAndInEnglish()
    {
        await SeedAsync();

        await using var db = Db();
        var accounts = await db.Users
            .Where(user => user.Id == PrimaryUserId || user.Id == SecondaryUserId || user.Id == AdminUserId)
            .OrderBy(user => user.Id)
            .ToListAsync(CancellationToken);

        accounts.Should().HaveCount(3);
        accounts.Should().OnlyContain(user => user.EmailConfirmed);
        // The whole UI reads its locale off this column; a Slovak default breaks every label locator.
        accounts.Should().OnlyContain(user => user.Locale == AvailableLocales.En);
        accounts[0].Email.Should().Be(PrimaryUserEmail);
        accounts[1].Email.Should().Be(SecondaryUserEmail);
        accounts[2].Email.Should().Be(AdminUserEmail);
    }

    /// <summary>
    /// The role rows, asserted from both sides — because both sides are load-bearing and neither is visible
    /// to the client. <c>GET /api/user/data</c> returns no roles or claims field at all, so the first place a
    /// missing grant shows up is a 403 from a route the SPA has already rendered the chrome for; that is
    /// precisely how the reminders register came to answer 403 for every fixture account.
    ///
    /// <para>The "only the admin" half matters as much as the grant: the primary account is what nearly every
    /// spec signs in as, and if it ever picked up <c>Admin</c> then no spec could tell an admin-gated route
    /// from an open one again.</para>
    /// </summary>
    [Fact]
    public async Task Seed_GivesTheAdminAccountTheAdminRole_AndNoOtherAccount()
    {
        await SeedAsync();

        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Core.domain.model.entity.user.User>>();

        var admin = await users.FindByEmailAsync(AdminUserEmail);
        admin.Should().NotBeNull();
        (await users.CheckPasswordAsync(admin!, AdminUserPassword)).Should().BeTrue();

        var adminRoles = await users.GetRolesAsync(admin!);
        adminRoles.Should().Contain(PortalRoleCatalog.Admin,
            "the five reminder-register and reminder-dashboard routes are gated on GetAdminRole()");
        adminRoles.Should().Contain(PortalRoleCatalog.User,
            "an administrator is also an ordinary user of the app, and needs their own surface to navigate");

        foreach (var email in new[] { PrimaryUserEmail, SecondaryUserEmail })
        {
            var plain = await users.FindByEmailAsync(email);
            (await users.GetRolesAsync(plain!)).Should().BeEquivalentTo([PortalRoleCatalog.User],
                "a plain fixture account being denied admin surface is the contrast the admin account exists against");
        }
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

        var anchor = await db.Set<MemoryAnchor>().IgnoreQueryFilters()
            .SingleAsync(a => a.Id == MemoryAnchorLearnBouldering, CancellationToken);
        anchor.ActivityId.Should().Be(bucketList.ActivityId,
            "the anchor closes the loop on a one-time bucket-list entry");
        anchor.Rating.Should().BeInRange(1, 10);

        // The second anchor is the other half of the fixture's anchor story: one on a live activity, one on
        // the archived one. Anchoring something and retiring the activity afterwards is the normal path, so
        // a fixture holding only the live case leaves the anchor grid's archived lift unexercised — which is
        // how that grid shipped counting a row it could not project.
        var onArchived = await db.Set<MemoryAnchor>().IgnoreQueryFilters()
            .SingleAsync(a => a.Id == MemoryAnchorRetiredHobby, CancellationToken);
        onArchived.ActivityId.Should().Be(ActivityRetiredHobby);
        onArchived.Rating.Should().BeInRange(1, 10);

        (anchor.AnchorYear, anchor.AnchorMonth).Should().NotBe((onArchived.AnchorYear, onArchived.AnchorMonth),
            "two anchors in distinct periods are what makes the grid's periodKey sort assert on something");
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
    /// <para>
    /// Rendered in Slovak on purpose, whatever the fixture account's locale: the fallback sentence is asserted
    /// by its Slovak spelling, so the check has to run in the language it is spelled in.
    /// </para>
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
            var (title, bodyText) = renderer.Render(notification.Type, notification.PayloadJson, AvailableLocales.Sk);

            title.Should().NotBeNullOrWhiteSpace();
            bodyText.Should().NotBe("Máte novú notifikáciu.",
                $"the payload seeded for {notification.Type} has to bind to the record that type declares");
        }

        // And the one kind whose payload carries a concrete value renders that value rather than its fallback.
        var digest = notifications.Single(notification => notification.Type == NotificationType.ReminderDigest);
        renderer.Render(digest.Type, digest.PayloadJson, AvailableLocales.Sk).Body.Should().Contain("3");
    }

    // --- fixture item 12: the reminders register and its dashboards -------------------------------

    /// <summary>
    /// Unit 17's five views had nothing to show even once the role gate was satisfiable: the fixture created
    /// no reminder at all, in either of the two projects that own half of one. So this asserts the module
    /// side has rows <i>and</i> that they are not all the same row — every dashboard number the overview
    /// renders is a count over a subset, and a fixture of six identical active definitions makes five of
    /// those numbers indistinguishable from a stub that returns the total.
    /// </summary>
    [Fact]
    public async Task Seed_LeavesReminderDefinitions_InEveryLifecycleStateTheDashboardsCount()
    {
        var body = await SeedAsync();
        var now = DateTimeOffset.UtcNow;

        await using var db = Db();

        var definitions = await db.Set<ReminderDefinition>().ToListAsync(CancellationToken);

        definitions.Should().HaveCountGreaterThanOrEqualTo(5);
        body.Counts.ReminderDefinitions.Should().Be(definitions.Count);

        // Upcoming is defined as "has a pending occurrence", and the paused/cancelled rows are what make it a
        // strictly smaller set than the total — the registry nulls the column on both transitions.
        var upcoming = definitions.Where(definition => definition.NextOccurrenceAt != null).ToList();
        upcoming.Should().HaveCountGreaterThan(1);
        upcoming.Count.Should().BeLessThan(definitions.Count);
        body.Counts.UpcomingReminders.Should().Be(upcoming.Count);

        definitions.Should().Contain(definition => definition.Status == ReminderStatus.Paused)
            .And.Contain(definition => definition.Status == ReminderStatus.Cancelled);
        definitions.Where(definition => definition.Status != ReminderStatus.Active)
            .Should().OnlyContain(definition => definition.NextOccurrenceAt == null);

        // The overview's headline card counts the next seven days, not the whole upcoming set. One reminder
        // deliberately falls outside that window so the two numbers differ.
        upcoming.Should().Contain(definition => definition.NextOccurrenceAt <= now.AddDays(7))
            .And.Contain(definition => definition.NextOccurrenceAt > now.AddDays(7));

        // Both schedule shapes, because the grid renders a schedule-type column and filters on it.
        definitions.Should().Contain(definition => definition.ScheduleType == ReminderScheduleType.OneShot)
            .And.Contain(definition => definition.ScheduleType == ReminderScheduleType.RecurringInterval);

        // Across both accounts: these views roll up every user's reminders, which is the whole reason they
        // are Admin-only and not the self-service my-upcoming view.
        var recipients = await db.Set<ReminderRecipient>()
            .Select(recipient => recipient.UserId)
            .Distinct()
            .ToListAsync(CancellationToken);
        recipients.Should().Contain([PrimaryUserId, SecondaryUserId]);
    }

    /// <summary>
    /// The two halves of a reminder live in two projects, and only the module half is keyed by strings. A
    /// definition whose <c>SubjectId</c> names a portal row that does not exist is exactly the orphan
    /// <c>ReminderRegistrationService</c> logs about — it renders in the register perfectly and is a state the
    /// app cannot reach.
    /// </summary>
    [Fact]
    public async Task Seed_KeysEveryDefinitionToARealPortalReminder()
    {
        var body = await SeedAsync();

        await using var db = Db();

        var reminderIds = await db.Set<Reminder>().IgnoreQueryFilters()
            .Select(reminder => reminder.Id)
            .ToListAsync(CancellationToken);

        var definitions = await db.Set<ReminderDefinition>().ToListAsync(CancellationToken);

        definitions.Should().OnlyContain(definition =>
            definition.OwnerModule == ReminderRegistrationService.OwnerModule
            && definition.SubjectType == ReminderRegistrationService.SubjectType
            && definition.Kind == ReminderRegistrationService.Kind
            && definition.TemplateKey == ReminderRegistrationService.TemplateKey);

        definitions.Select(definition => long.Parse(definition.SubjectId))
            .Should().BeSubsetOf(reminderIds)
            .And.OnlyHaveUniqueItems("the key is unique on (owner, subject type, subject id, kind)");

        // The response's two id maps name the same reminders from both sides, and differ — a spec handing the
        // portal id to /reminder-definition/{id} must not accidentally hit a real row.
        foreach (var (name, reminderId) in body.Reminders)
        {
            body.ReminderDefinitions[name].Should().NotBe(reminderId);
            definitions.Should().ContainSingle(definition => definition.Id == body.ReminderDefinitions[name])
                .Which.SubjectId.Should().Be(reminderId.ToString());
        }
    }

    /// <summary>
    /// The dispatch ledger behind the history grid and the overview's outcome card. The reversal pair is the
    /// part worth seeding deliberately: "needs attention" is Failed rows <i>no reversal supersedes</i>, so a
    /// fixture with one plain failure cannot distinguish that from a count of all failures.
    /// </summary>
    [Fact]
    public async Task Seed_LeavesEveryDispatchOutcome_IncludingAFailureThatWasReversed()
    {
        var body = await SeedAsync();
        var today = body.SeedDateUtc;

        await using var db = Db();

        var dispatches = await db.Set<ReminderDispatch>().ToListAsync(CancellationToken);

        dispatches.Should().NotBeEmpty();
        body.Counts.ReminderDispatches.Should().Be(dispatches.Count);

        foreach (var outcome in Enum.GetValues<DispatchOutcome>())
            dispatches.Should().Contain(dispatch => dispatch.Outcome == outcome,
                $"the overview's recent-outcome card has a tile for {outcome}");

        // Inside the overview's backward window, or the card tallies zeroes over a populated ledger.
        dispatches.Should().OnlyContain(dispatch => dispatch.DispatchedAt >= new DateTimeOffset(
            today.AddDays(-7).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

        var reversedIds = dispatches.Where(dispatch => dispatch.ReversesDispatchId != null)
            .Select(dispatch => dispatch.ReversesDispatchId!.Value)
            .ToList();
        reversedIds.Should().NotBeEmpty();

        var failures = dispatches.Where(dispatch => dispatch.Outcome == DispatchOutcome.Failed).ToList();
        var effective = failures.Where(dispatch => !reversedIds.Contains(dispatch.Id)).ToList();

        effective.Should().NotBeEmpty();
        effective.Count.Should().BeLessThan(failures.Count,
            "a reversed failure is what makes the needs-attention count different from the failure count");
        body.Counts.ReminderFailuresNeedingAttention.Should().Be(effective.Count);

        // The failure detail is the fixed-shape summary the card renders, never an exception message — this
        // ledger is retained and exported, and free-text PII cannot be scrubbed out of it afterwards.
        failures.Should().OnlyContain(dispatch => dispatch.FailureDetail != null);
        dispatches.Where(dispatch => dispatch.Outcome == DispatchOutcome.Skipped)
            .Should().OnlyContain(dispatch => dispatch.SkipReason != null);

        // Recipient snapshots carry ids only.
        dispatches.Should().OnlyContain(dispatch => dispatch.RecipientsSnapshot.StartsWith("["));
    }

    /// <summary>
    /// The rows above prove the register has content; this proves it renders as content. Same reasoning as the
    /// estimate-vs-actual projection guard — it goes through <c>ReminderDefinitionDto.Projection</c>, the
    /// expression the grid endpoint itself uses, because a row the projection cannot produce is invisible to
    /// every spec while passing every row assertion in this class.
    /// </summary>
    [Fact]
    public async Task Seed_LeavesTheRegisterRenderableThroughItsProjection()
    {
        var body = await SeedAsync();

        await using var db = Db();

        var projected = await ReminderDefinitionDto.Projection(db.Set<ReminderDefinition>())
            .ToListAsync(CancellationToken);

        projected.Should().HaveCount(body.Counts.ReminderDefinitions);

        // Every column the register's grid shows: owner module, subject, kind, schedule type, status and the
        // next occurrence. All six rendered a dash or a blank when the grid had no rows to show at all.
        projected.Should().OnlyContain(definition =>
            !string.IsNullOrWhiteSpace(definition.OwnerModule)
            && !string.IsNullOrWhiteSpace(definition.SubjectType)
            && !string.IsNullOrWhiteSpace(definition.SubjectId)
            && !string.IsNullOrWhiteSpace(definition.Kind));

        // Recipients come through the projection as their own collection; an empty one renders a reminder
        // nobody receives.
        projected.Should().OnlyContain(definition => definition.Recipients.Count > 0);

        // And the one-shot rows carry the lead offsets the detail view lists.
        projected.Where(definition => definition.ScheduleType == ReminderScheduleType.OneShot)
            .Should().OnlyContain(definition => definition.DueAt != null && definition.LeadOffsets.Count > 0);

        projected.Where(definition => definition.ScheduleType == ReminderScheduleType.RecurringInterval)
            .Should().OnlyContain(definition => definition.IntervalPreset != null && definition.AnchorDate != null);
    }

    // --- fixture item 14: routine period history ---------------------------------------------------

    /// <summary>
    /// The completion ledger and the period-level streaks. Four routine surfaces (heatmaps, trend, personal
    /// bests, the review card) read these and had never rendered, because the per-user default seeder creates
    /// the four periods bare and nothing had ever closed a cycle. Asserted as the rules the reset job would
    /// have obeyed, not just as a row count: the Weekly history is deep enough to fill its heatmap, mixed,
    /// contiguous, and carries a frozen cell; and the streak on each period is what the freeze endpoint's own
    /// walk derives from the rows — a period whose streak disagrees with its history gets silently "corrected"
    /// the first time a freeze is spent.
    /// </summary>
    [Fact]
    public async Task Seed_LeavesRoutinePeriodHistory_MixedFrozenAndConsistentWithTheStreaks()
    {
        var body = await SeedAsync();

        await using var db = Db();

        var weekly = await db.RoutineTimePeriods.IgnoreQueryFilters()
            .SingleAsync(period => period.Id == body.RoutineTimePeriods["Weekly"], CancellationToken);
        var daily = await db.RoutineTimePeriods.IgnoreQueryFilters()
            .SingleAsync(period => period.Id == body.RoutineTimePeriods["Daily"], CancellationToken);

        var all = await db.Set<RoutinePeriodCompletion>()
            .Where(completion => completion.RoutineTimePeriod.UserId == PrimaryUserId)
            .OrderBy(completion => completion.PeriodStart)
            .ToListAsync(CancellationToken);

        all.Should().NotBeEmpty();
        body.Counts.RoutinePeriodCompletions.Should().Be(all.Count);
        body.Counts.FrozenRoutinePeriodCompletions.Should().Be(all.Count(completion => completion.IsFrozen));

        foreach (var period in new[] { weekly, daily })
        {
            var rows = all.Where(completion => completion.TimePeriodId == period.Id).ToList();

            // Enough to fill the heatmap at the depth the server reports for it, or the strip is left-padded.
            rows.Should().HaveCountGreaterThanOrEqualTo(period.HistoryDepth,
                $"the {period.Text} heatmap renders HistoryDepth cells and a shorter history leaves blanks");

            // Mixed: the consistency figure and the trend arrow need both outcomes to compute anything.
            rows.Should().Contain(row => RoutineStreakFreezeService.IsSuccess(row, period.StreakThreshold))
                .And.Contain(row => !RoutineStreakFreezeService.IsSuccess(row, period.StreakThreshold));

            // Contiguous, oldest to newest, one row per cycle — a gap renders as a hole in the heatmap and the
            // history dialog walks day by day between periodStart and periodEnd.
            rows.Should().OnlyContain(row => row.PeriodEnd == row.PeriodStart.AddDays(period.LengthInDays));
            rows.Zip(rows.Skip(1)).Should().OnlyContain(pair => pair.Second.PeriodStart == pair.First.PeriodEnd);

            // The newest row closes exactly at the instant the period says it last reset. That is the pairing
            // the reset job leaves behind, and it is what stops the grouped read closing the cycle again.
            rows[^1].PeriodEnd.Should().Be(DateOnly.FromDateTime(period.LastResetAt!.Value));

            // The streak the rows imply is the streak the period carries.
            var walked = new RoutineTimePeriod
            {
                Text = period.Text, Color = period.Color, LengthInDays = period.LengthInDays,
                ResetAnchorDay = period.ResetAnchorDay, StreakThreshold = period.StreakThreshold,
                StreakGraceDays = period.StreakGraceDays
            };
            RoutineStreakFreezeService.RecomputeStreak(walked, rows);
            period.Streak.Should().Be(walked.Streak,
                $"the {period.Text} period's streak has to be the one its own history walks to");
            period.BestStreak.Should().BeGreaterThanOrEqualTo(walked.BestStreak);
        }

        // The third cell state, reachable only from a real row — the SPA's mock history never invents one.
        var frozen = all.Where(row => row.IsFrozen).ToList();
        frozen.Should().ContainSingle().Which.TimePeriodId.Should().Be(weekly.Id);
        frozen.Should().OnlyContain(row => row.FrozenAt != null);
        all.Where(row => !row.IsFrozen).Should().OnlyContain(row => row.FrozenAt == null);

        // A frozen row keeps the counts it was recorded with; only its scoring changes.
        frozen.Should().OnlyContain(row => !RoutineStreakFreezeService.IsSuccess(row, weekly.StreakThreshold));

        // And a miss that was *not* frozen stays in the Weekly history, or the freeze action has nothing to act on.
        all.Should().Contain(row => row.TimePeriodId == weekly.Id && !row.IsFrozen
                                    && !RoutineStreakFreezeService.IsSuccess(row, weekly.StreakThreshold));

        // The freeze budget is consistent with the ledger: a freeze spent inside the current window is one
        // fewer remaining, and the window itself is the one RefreshFreezeBudget would open.
        var windowStart = new DateTime(body.SeedDateUtc.Year, body.SeedDateUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var spentThisWindow = frozen.Count(row => row.FrozenAt >= windowStart);
        weekly.FreezesRemaining.Should().Be(weekly.FreezeBudget - spentThisWindow);
        weekly.FreezeBudgetResetsAt.Should().Be(RoutineStreakFreezeService.NextBudgetReset(windowStart));
    }

    /// <summary>
    /// The one that pins the silent failure. <c>GET /routine-todo-list/grouped-by-time-period</c> runs the
    /// reset catch-up over every period before rendering, so a fixture whose <c>LastResetAt</c> makes a period
    /// look due is rewritten by the first request: a completion row is appended, the streak re-scored, every
    /// item un-ticked — all on a 200, and every row assertion above still passes against the pre-request
    /// state. This runs the same three lazy mutations the read runs and asserts each is a no-op.
    /// </summary>
    [Fact]
    public async Task Seed_LeavesEveryRoutinePeriodCurrent_SoTheGroupedReadDoesNotRewriteIt()
    {
        await SeedAsync();
        var now = DateTime.UtcNow;

        await using var db = Db();

        var periods = await db.RoutineTimePeriods.IgnoreQueryFilters()
            .Where(period => period.UserId == PrimaryUserId)
            .Include(period => period.RoutineTodoListColl)
            .ToListAsync(CancellationToken);

        periods.Should().HaveCount(4);

        foreach (var period in periods)
        {
            RoutineResetService.ComputeNextReset(period, now).Should().BeAfter(now,
                $"the {period.Text} period must not be due, or the first grouped read closes a cycle the fixture did not seed");

            RoutineResetService.CheckGrace(period, now).Should().BeFalse();
            RoutineStreakFreezeService.RefreshFreezeBudget(period, now).Should().BeFalse(
                $"the {period.Text} period's freeze window has to be the one a read would open, or the first read writes");
            RoutineResetService.TryResetCatchUp(period, period.RoutineTodoListColl.ToList(), now).Should().BeNull();

            // Each item's own cycle marker agrees with its period's: LastResetDate on the boundary that opened
            // the current cycle, and LastCompletedAt inside it exactly when the item is done. That pair is how
            // UpdateItemStreak decides whether the next tick scores, so a disagreement double-counts or refuses.
            var cycleStart = period.LastResetAt is { } lastReset ? DateOnly.FromDateTime(lastReset) : (DateOnly?)null;
            foreach (var item in period.RoutineTodoListColl)
            {
                if (cycleStart is null)
                    continue;

                item.LastResetDate.Should().Be(cycleStart, $"item {item.Id} sits in the {period.Text} period");
                (DateOnly.FromDateTime(item.LastCompletedAt) >= cycleStart).Should().Be(item.IsDone,
                    $"item {item.Id} is {(item.IsDone ? "done" : "not done")} this cycle, so its last completion must {(item.IsDone ? "" : "not ")}fall inside it");
            }
        }
    }

    /// <summary>
    /// <c>RoutineTodoListItem.vue</c> picks its milestone icon from the item's <i>current</i> streak at
    /// 7 / 30 / 90 / 180 / 365. The highest seeded streak was 12, so four of the five branches had never
    /// rendered for anyone. One item per tier, and the response names each by the tier it clears.
    /// </summary>
    [Fact]
    public async Task Seed_LeavesAnItemStreakInEveryMilestoneTier()
    {
        var body = await SeedAsync();

        await using var db = Db();

        var routines = await db.RoutineTodoLists.IgnoreQueryFilters()
            .Where(routine => routine.UserId == PrimaryUserId)
            .ToDictionaryAsync(routine => routine.Id, CancellationToken);

        foreach (var (tier, key) in new[] { (7, "daily"), (30, "milestone30"), (90, "milestone90"), (180, "milestone180"), (365, "milestone365") })
        {
            var routine = routines[body.Routines[key]];
            routine.Streak.Should().BeGreaterThanOrEqualTo(tier, $"routines.{key} is the fixture's ≥{tier} item");
            routine.BestStreak.Should().BeGreaterThanOrEqualTo(routine.Streak, "a best below the current streak is a state no toggle produces");
        }

        // Distinct tiers, not one item clearing all of them: the icon is a switch on the highest tier reached,
        // so five items at 400 would render one crown five times.
        routines.Values.Select(routine => routine.Streak switch
            {
                >= 365 => 365, >= 180 => 180, >= 90 => 90, >= 30 => 30, >= 7 => 7, _ => 0
            })
            .Distinct()
            .Should().Contain([7, 30, 90, 180, 365]);
    }

    /// <summary>
    /// The rows above prove the ledger is stored; this proves the client is handed it. The grouped read and
    /// the flat reads both render a period through <c>RoutineTimePeriodResponse.From</c>, and the flat ones go
    /// through <c>RoutineTimePeriodReadRefresher</c> — so this runs that refresher over the fixture and checks
    /// the response carries the history, the frozen flag, and the streak the rows were seeded to agree with.
    /// Same reason as the estimate and the register: a field on the entity and on no response is invisible to
    /// every spec while every row assertion passes.
    /// </summary>
    [Fact]
    public async Task Seed_LeavesTheRoutineHistoryVisibleThroughTheReadPath()
    {
        var body = await SeedAsync();
        var now = DateTime.UtcNow;

        // The refresher scopes nothing itself and relies on the ambient IEntityWithUser filter. Every stock test
        // host — the unauthenticated one included — resolves a user id for that filter, so from any of them the
        // fixture's periods are invisible and the read renders nothing. A host with no ambient user at all is
        // the state ScopeUserId documents as "apply no scoping", which is what the fixture accounts need here.
        await using var host = CreateFactory(roles: null, userId: null,
            configureServices: services => services.AddScoped<ILoggedUserService, NoAmbientUserService>());
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ids = new[] { body.RoutineTimePeriods["Weekly"], body.RoutineTimePeriods["Daily"] };
        var rendered = await RoutineTimePeriodReadRefresher.RefreshAndRenderAsync(db, ids, now, CancellationToken);

        rendered.Should().ContainKeys(ids);

        var weekly = rendered[body.RoutineTimePeriods["Weekly"]];
        weekly.CompletionHistory.Should().HaveCount(weekly.HistoryDepth,
            "the read trims to HistoryDepth, and the fixture is seeded exactly that deep");
        weekly.CompletionHistory.Should().Contain(record => record.IsFrozen)
            .And.Contain(record => !record.IsFrozen && record.CompletedCount < record.TotalCount)
            .And.Contain(record => record.CompletedCount == record.TotalCount);
        weekly.CompletionHistory.Should().BeInAscendingOrder(record => record.PeriodStart);
        weekly.Streak.Should().BeGreaterThan(0);
        weekly.BestStreak.Should().BeGreaterThan(weekly.Streak, "the personal-bests panel shows a best that differs from the current run");
        weekly.NextResetAt.Should().BeAfter(now);
        weekly.FreezesRemaining.Should().BeGreaterThan(0, "the freeze affordance is hidden at zero");

        // The two totals the SPA gates three surfaces on. Elapsed is the window, completed is the subset that
        // met the threshold — strictly between zero and elapsed, or the consistency figure is 0% or 100% and
        // proves nothing. The frozen cycle counts as elapsed and not as completed: it kept its 0 of 1.
        weekly.TotalPeriodsElapsed.Should().Be(weekly.CompletionHistory.Count);
        weekly.TotalPeriodsCompleted.Should().Be(
            weekly.CompletionHistory.Count(record => record.TotalCount > 0
                && (double)record.CompletedCount / record.TotalCount * 100 >= weekly.StreakThreshold));
        weekly.TotalPeriodsCompleted.Should().BeGreaterThan(0).And.BeLessThan(weekly.TotalPeriodsElapsed);

        var daily = rendered[body.RoutineTimePeriods["Daily"]];
        daily.CompletionHistory.Should().NotBeEmpty();
        daily.Streak.Should().BeGreaterThan(0);
        daily.TotalPeriodsElapsed.Should().Be(daily.CompletionHistory.Count);
        daily.TotalPeriodsCompleted.Should().Be(daily.TotalPeriodsElapsed - 1, "one Daily cycle fell short");
    }

    // --- scheduler ----------------------------------------------------------------------------------

    /// <summary>
    /// The orphaned job is only worth seeding if the reads report it as orphaned — that is a fact about the
    /// handlers the running host resolved, not about any column, so a row assertion alone proves nothing. This
    /// goes through the three reads the SPA's orphan surfaces render from: the health lists, the job by id, and
    /// the run detail's replay preconditions.
    /// </summary>
    [Fact]
    public async Task Seed_LeavesAnOrphanedJob_ThatTheScheduledReadsReportAsOrphaned()
    {
        var body = await SeedAsync();
        var jobId = body.Scheduler["orphanedJob"];
        var runId = body.Scheduler["orphanedJobRun"];

        using var client = CreateUserRoleClient();

        var health = await client.GetFromJsonAsync<JsonElement>("api/scheduler-dashboard/health", JsonOpts, CancellationToken);
        bool Lists(string list) => health.GetProperty(list).EnumerateArray()
            .Any(row => row.GetProperty("jobKey").GetString() == OrphanedJobKey);

        Lists("orphanedJobs").Should().BeTrue("its handler key resolves to no registered handler");
        Lists("overdueJobs").Should().BeFalse("NextRunAt is null — the row is for the orphan list alone");
        Lists("failedJobs").Should().BeFalse("its last run succeeded");

        var job = await client.GetFromJsonAsync<JsonElement>($"api/scheduled-job/{jobId}", JsonOpts, CancellationToken);
        job.GetProperty("jobKey").GetString().Should().Be(OrphanedJobKey);
        job.GetProperty("isOrphaned").GetBoolean().Should().BeTrue("job detail gates Trigger now on this flag");

        var run = await client.GetFromJsonAsync<JsonElement>($"api/scheduler-dashboard/run/{runId}", JsonOpts, CancellationToken);
        run.GetProperty("isJobOrphaned").GetBoolean().Should().BeTrue("run detail gates Replay on this flag");
        run.GetProperty("jobStatus").GetString().Should().Be(nameof(JobStatus.Active),
            "Removed would gate Replay too, and for a different reason the page words differently");
    }

    /// <summary>
    /// The scheduler rows are upserted rather than truncated and re-inserted, so a second reset rewrites the same
    /// two rows — no second orphan, no second run, and the ids a spec read off the first response still hold.
    /// </summary>
    [Fact]
    public async Task Seed_Twice_KeepsOneOrphanedJobAndOneRun_UnderTheSameIds()
    {
        var first = await SeedAsync();
        var second = await SeedAsync();

        second.Scheduler.Should().BeEquivalentTo(first.Scheduler);

        await using var db = Db();
        (await db.ScheduledJobs.IgnoreQueryFilters().CountAsync(job => job.JobKey == OrphanedJobKey, CancellationToken))
            .Should().Be(1);
        (await db.ScheduledJobRuns.CountAsync(run => run.ScheduledJobId == first.Scheduler["orphanedJob"], CancellationToken))
            .Should().Be(1);
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

    /// <summary>
    /// <c>BaseDbContext.ScopeUserId</c> is <c>IsAuthenticated ? GetUserId : null</c>, so this is the only way to
    /// reach the null it documents as "apply no scoping" — the state every read of the fixture accounts from
    /// inside a test host needs, since none of them is the identity a stock host resolves.
    /// </summary>
    private sealed class NoAmbientUserService : ILoggedUserService
    {
        public ClaimsPrincipal? LoggedUserPrincipal => null;
        public bool IsAuthenticated => false;
        public IEnumerable<string> GetRoles => [];
        public string GetEmail => throw new InvalidOperationException("No user is signed in.");
        public long GetUserId => throw new InvalidOperationException("No user is signed in.");
        public long? GetUserIdOrNull => null;
    }
}
