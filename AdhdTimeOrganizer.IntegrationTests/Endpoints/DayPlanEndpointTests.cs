using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// <c>GET /calendar/day-plan/{date}</c> — the one-request day plan that replaced the serialised
/// <c>by-Date</c> → <c>planner-task/filter</c> pair on the home page.
/// <para>
/// What is pinned here is everything that would fail <b>quietly</b>. The status code for an unplanned day is
/// the whole point of the contract and is invisible from the server side — a 404 there does not throw, it
/// just turns the client's "plan today" empty state into an error banner. The user scoping has no safety net
/// worth trusting either: the calendar id is resolved on the server now, so a cross-user read would show up
/// as somebody else's tasks rendered on your own home page rather than as an exception. And the streak has to
/// survive a null calendar, which is exactly the case the by-Date route never had to handle.
/// </para>
/// </summary>
[Collection("Postgres")]
public class DayPlanEndpointTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    /// <summary>
    /// The composition itself: one request, both halves of the day, ordered. The ordering matters because the
    /// old pair got it from <c>AlwaysSortBy</c> on the filter endpoint, and the projection here turns
    /// <c>StartTime</c> into a <c>TimeDto</c> that no longer sorts in the database.
    /// </summary>
    [Fact]
    public async Task DayPlan_ReturnsTheCalendarAndItsTasks_InStartTimeOrder()
    {
        var activityId = await SeedActivityAsync("day-plan-basic");
        var date = new DateOnly(2026, 3, 4);
        var calendarId = await SeedCalendarAsync(date);

        await SeedTaskAsync(activityId, calendarId, new TimeOnly(15, 0), new TimeOnly(16, 0));
        await SeedTaskAsync(activityId, calendarId, new TimeOnly(8, 30), new TimeOnly(9, 0));
        await SeedTaskAsync(activityId, calendarId, new TimeOnly(12, 0), new TimeOnly(13, 0));

        var plan = await GetPlanAsync(date);

        plan.Date.Should().Be(date);
        plan.Calendar.Should().NotBeNull();
        plan.Calendar!.Id.Should().Be(calendarId);
        plan.HasPlan.Should().BeTrue();
        plan.Tasks.Select(t => t.StartTime.Hours).Should().Equal(8, 12, 15);
        plan.Tasks.Should().OnlyContain(t => t.CalendarId == calendarId);
    }

    /// <summary>
    /// <b>The ruling this endpoint exists to make.</b> A day the user never planned is a successful empty
    /// result, not a 404 — the sibling <c>by-Date</c> route answers 404 there, and a client that treats a
    /// rejected request as an error (which is the only thing it can do) renders "could not load your plan"
    /// with a retry button over a day that is merely empty, and can never reach the "plan today" state.
    /// </summary>
    [Fact]
    public async Task DayPlan_ForADateWithNoCalendar_Is200WithNothingInIt_NotA404()
    {
        // Outside the seeded years, so no CalendarSeeder row can exist for it.
        var unplanned = new DateOnly(2031, 7, 9);

        var client = CreateClient();
        var response = await client.GetAsync($"/api/calendar/day-plan/{unplanned:yyyy-MM-dd}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "an unplanned day is not a failed request");

        var plan = await response.Content.ReadFromJsonAsync<DayPlanResponse>(JsonOpts, CancellationToken);
        plan!.Calendar.Should().BeNull();
        plan.Tasks.Should().BeEmpty();
        plan.HasPlan.Should().BeFalse();
        plan.Date.Should().Be(unplanned, "the date still comes back, so the client need not remember what it asked");
        plan.Streak.Should().NotBeNull("the streak is a fact about the user and survives a day with no calendar");
    }

    /// <summary>
    /// A calendar row with no tasks is planned-in-name-only. <c>HasPlan</c> has to say false there, because
    /// <c>CalendarSeeder</c> bulk-creates a row for every date of the seeded years whether the user ever
    /// touched it or not — so calendar presence answers "is this date in the seeded window", never "did the
    /// user plan it", and a client branching on presence would never show its empty state.
    /// </summary>
    [Fact]
    public async Task DayPlan_ForASeededButEmptyDay_HasPlanIsFalse()
    {
        var date = new DateOnly(2026, 3, 5);
        await SeedCalendarAsync(date);

        var plan = await GetPlanAsync(date);

        plan.Calendar.Should().NotBeNull("the row exists");
        plan.Tasks.Should().BeEmpty();
        plan.HasPlan.Should().BeFalse("an untouched seeded row is not a plan");
    }

    /// <summary>
    /// The client no longer supplies the calendar id, so the server resolving it is the only thing standing
    /// between one user's home page and another user's day. Nothing here throws if the scoping is lost — the
    /// wrong day simply renders.
    /// </summary>
    [Fact]
    public async Task DayPlan_NeverReachesAnotherUsersDay()
    {
        var date = new DateOnly(2026, 3, 6);
        // A real second user row, not just an id: the UserId FK is NOT NULL and enforced.
        long otherUserId;
        await using (var db = CreateDbContext())
            otherUserId = (await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken)).Id;

        var mine = await SeedActivityAsync("day-plan-mine");
        var myCalendar = await SeedCalendarAsync(date);
        await SeedTaskAsync(mine, myCalendar, new TimeOnly(9, 0), new TimeOnly(10, 0));

        var theirs = await SeedActivityAsync("day-plan-theirs", otherUserId);
        var theirCalendar = await SeedCalendarAsync(date, otherUserId);
        await SeedTaskAsync(theirs, theirCalendar, new TimeOnly(11, 0), new TimeOnly(12, 0), otherUserId);

        var plan = await GetPlanAsync(date);

        plan.Calendar!.Id.Should().Be(myCalendar);
        plan.Tasks.Should().HaveCount(1);
        plan.Tasks.Single().StartTime.Hours.Should().Be(9);
    }

    /// <summary>
    /// The streak used to ride on <c>CalendarResponse</c>; here it is its own field so that a null calendar
    /// cannot take it down with it. The nested copy is nulled to keep one source of it in the payload — if it
    /// came back on both, the two would eventually be filled in by different code paths and disagree.
    /// </summary>
    [Fact]
    public async Task DayPlan_CarriesTheStreakAtTheTopLevel_AndNotAlsoOnTheCalendar()
    {
        var activityId = await SeedActivityAsync("day-plan-streak");
        var date = new DateOnly(2026, 3, 7);
        var calendarId = await SeedCalendarAsync(date);
        await SeedTaskAsync(activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0));

        var plan = await GetPlanAsync(date);

        plan.Streak.Should().NotBeNull();
        plan.Streak.Timezone.Should().NotBeNullOrWhiteSpace();
        plan.Calendar!.Streak.Should().BeNull("the top-level field is the only copy");
    }

    /// <summary>
    /// ISO is the format the ask specified and the one a client holding a local date already has; the
    /// sibling route's <c>dd-MM-yyyy</c> is accepted too so that a URL copied between the two cannot silently
    /// resolve to a different day. Both have to land on the same date — <c>04-03-2026</c> read as ISO would be
    /// a parse failure, but <c>03-04-2026</c> style ambiguity is exactly what this guards.
    /// </summary>
    [Fact]
    public async Task DayPlan_AcceptsBothDateFormats_AndRejectsAnythingElse()
    {
        var date = new DateOnly(2026, 3, 4);
        await SeedCalendarAsync(date);

        var client = CreateClient();

        var iso = await client.GetAsync("/api/calendar/day-plan/2026-03-04", CancellationToken);
        var legacy = await client.GetAsync("/api/calendar/day-plan/04-03-2026", CancellationToken);
        var nonsense = await client.GetAsync("/api/calendar/day-plan/next-tuesday", CancellationToken);

        iso.StatusCode.Should().Be(HttpStatusCode.OK);
        legacy.StatusCode.Should().Be(HttpStatusCode.OK);
        nonsense.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var fromIso = await iso.Content.ReadFromJsonAsync<DayPlanResponse>(JsonOpts, CancellationToken);
        var fromLegacy = await legacy.Content.ReadFromJsonAsync<DayPlanResponse>(JsonOpts, CancellationToken);
        fromIso!.Date.Should().Be(date);
        fromLegacy!.Date.Should().Be(date);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private async Task<DayPlanResponse> GetPlanAsync(DateOnly date)
    {
        var client = CreateClient();
        var response = await client.GetAsync($"/api/calendar/day-plan/{date:yyyy-MM-dd}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<DayPlanResponse>(JsonOpts, CancellationToken);
        body.Should().NotBeNull();
        return body!;
    }

    private async Task<long> SeedActivityAsync(string name, long? userId = null)
    {
        var owner = userId ?? UserId;
        await using var db = CreateDbContext();

        var role = new ActivityRole { UserId = owner, Name = $"{name}-role", Color = "#123456" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity { UserId = owner, Name = name, RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);
        return activity.Id;
    }

    private async Task<long> SeedCalendarAsync(DateOnly date, long? userId = null)
    {
        var owner = userId ?? UserId;
        await using var db = CreateDbContext();

        var existing = await db.Set<Calendar>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.UserId == owner && c.Date == date, CancellationToken);

        if (existing != null)
            return existing.Id;

        var calendar = new Calendar
        {
            UserId = owner,
            Date = date,
            DayType = DayType.Workday,
            WakeUpTime = new TimeOnly(7, 0),
            BedTime = new TimeOnly(23, 0)
        };
        db.Set<Calendar>().Add(calendar);
        await db.SaveChangesAsync(CancellationToken);
        return calendar.Id;
    }

    private async Task<long> SeedTaskAsync(
        long activityId,
        long calendarId,
        TimeOnly start,
        TimeOnly end,
        long? userId = null)
    {
        await using var db = CreateDbContext();

        var task = new PlannerTask
        {
            UserId = userId ?? UserId,
            ActivityId = activityId,
            CalendarId = calendarId,
            StartTime = start,
            EndTime = end,
            IsBackground = false,
            Status = PlannerTaskStatus.NotStarted
        };
        db.Set<PlannerTask>().Add(task);
        await db.SaveChangesAsync(CancellationToken);
        return task.Id;
    }
}
