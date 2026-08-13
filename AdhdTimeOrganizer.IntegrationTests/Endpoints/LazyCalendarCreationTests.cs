using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
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
/// Lazy calendar creation — planning a day the user has no calendar row for.
/// <para>
/// The defect: <c>CalendarSeeder</c> fills whole years at user setup and there has never been a
/// create-calendar endpoint, so past the seeded horizon every date resolved to no row and no write could
/// attach to it. The planner rendered a normal empty day that silently refused every task, with nothing in
/// the app able to make the missing row. Nothing threw and nothing logged — the day just stayed empty.
/// </para>
/// <para>
/// These dates are deliberately far outside any seeded window, which is what makes them the real case rather
/// than a simulation of it.
/// </para>
/// </summary>
[Collection("Postgres")]
public class LazyCalendarCreationTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    /// <summary>
    /// The whole point: a date with no calendar row accepts a task, and the row appears.
    /// </summary>
    [Fact]
    public async Task CreatingATaskByDate_PastTheSeededHorizon_CreatesTheDay()
    {
        var activityId = await SeedActivityAsync("lazy-basic");
        var date = new DateOnly(2033, 4, 12);

        await AssertNoCalendarAsync(date);

        var response = await PostTaskAsync(activityId, date);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var calendar = await FindCalendarAsync(date);
        calendar.Should().NotBeNull("the day has to exist for the task to hang off it");

        var plan = await GetPlanAsync(date);
        plan.HasPlan.Should().BeTrue();
        plan.Tasks.Should().HaveCount(1);
        plan.Calendar!.Id.Should().Be(calendar!.Id);
    }

    /// <summary>
    /// Second task, same day, one row. <c>(user_id, date)</c> is unique, so a provisioner that created blindly
    /// would turn the second task of every unseeded day into a 500.
    /// </summary>
    [Fact]
    public async Task CreatingASecondTaskOnTheSameDay_ReusesTheDay()
    {
        var activityId = await SeedActivityAsync("lazy-reuse");
        var date = new DateOnly(2033, 4, 13);

        (await PostTaskAsync(activityId, date, 9)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await PostTaskAsync(activityId, date, 11)).StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = CreateDbContext();
        var rows = await db.Set<Calendar>()
            .IgnoreQueryFilters()
            .CountAsync(c => c.UserId == UserId && c.Date == date, CancellationToken);

        rows.Should().Be(1);
        (await GetPlanAsync(date)).Tasks.Should().HaveCount(2);
    }

    /// <summary>
    /// A lazily created day is the same day the seeder would have made — day type and holiday name included.
    /// This is the drift guard: the two creators share <c>CalendarDayFactory</c>, and if they ever stop, a day
    /// planned in advance and the same day planned on the spot disagree about what it is, which no test that
    /// only checks "a row exists" would catch.
    /// </summary>
    [Theory]
    // Christmas Day 2033 — a Sunday, so weekend *and* a holiday name.
    [InlineData(2033, 12, 25, DayType.Weekend, "Prvý sviatok vianočný")]
    // A plain Tuesday.
    [InlineData(2033, 4, 19, DayType.Workday, null)]
    // Slovak Constitution Day 2034, on a Friday — a workday that still carries its holiday name.
    [InlineData(2034, 9, 1, DayType.Workday, "Deň Ústavy Slovenskej republiky")]
    public async Task ALazilyCreatedDay_CarriesTheSameMetadataTheSeederWouldHaveGivenIt(
        int year, int month, int day, DayType expectedDayType, string? expectedHoliday)
    {
        var activityId = await SeedActivityAsync($"lazy-meta-{year}-{month}-{day}");
        var date = new DateOnly(year, month, day);

        (await PostTaskAsync(activityId, date)).StatusCode.Should().Be(HttpStatusCode.Created);

        var calendar = await FindCalendarAsync(date);
        calendar!.DayType.Should().Be(expectedDayType);
        calendar.HolidayName.Should().Be(expectedHoliday);
        calendar.WakeUpTime.Should().Be(new TimeOnly(8, 0), "the default sleep window the seeder has always used");
        calendar.BedTime.Should().Be(new TimeOnly(0, 0));
    }

    /// <summary>
    /// Applying a day template is the other way a date gets its first task, so it provisions too. Worth its
    /// own test because it is a hand-written endpoint that resolved the calendar itself and 404'd — the exact
    /// shape of the original defect.
    /// </summary>
    [Fact]
    public async Task ApplyingATemplateByDate_CreatesTheDay()
    {
        var activityId = await SeedActivityAsync("lazy-template");
        var templateId = await SeedDayTemplateAsync();
        var date = new DateOnly(2033, 5, 3);

        await AssertNoCalendarAsync(date);

        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/calendar/apply-planner-template", new
        {
            TemplateId = templateId,
            Date = date,
            ConflictResolution = "Ignore",
            TasksFromTemplate = new[]
            {
                new
                {
                    StartTime = new { Hours = 9, Minutes = 0 },
                    EndTime = new { Hours = 10, Minutes = 0 },
                    IsBackground = false,
                    ActivityId = activityId,
                    Status = "NotStarted"
                }
            }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var calendar = await FindCalendarAsync(date);
        calendar.Should().NotBeNull();

        var plan = await GetPlanAsync(date);
        plan.Tasks.Should().HaveCount(1);
        plan.Tasks.Single().CalendarId.Should().Be(calendar!.Id,
            "the endpoint stamps its one resolved calendar onto every task it builds");
    }

    /// <summary>
    /// A day is named one way or the other, never both and never neither. Both is rejected rather than
    /// resolved by precedence: the two can disagree, and the loser is a task quietly filed on a day nobody
    /// asked for.
    /// </summary>
    [Fact]
    public async Task ATaskNamingItsDayTwice_OrNotAtAll_IsRejected()
    {
        var activityId = await SeedActivityAsync("lazy-ambiguous");
        var date = new DateOnly(2033, 4, 14);
        var calendarId = await SeedCalendarAsync(new DateOnly(2033, 4, 15));

        var client = CreateClient();

        var both = await client.PostAsJsonAsync("/api/planner-task",
            TaskBody(activityId, 9, date: date, calendarId: calendarId), JsonOpts, CancellationToken);
        var neither = await client.PostAsJsonAsync("/api/planner-task",
            TaskBody(activityId, 9), JsonOpts, CancellationToken);

        both.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        neither.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The calendar id path is untouched — it is what the day-planner view still sends, and it must keep
    /// behaving exactly as it did.
    /// </summary>
    [Fact]
    public async Task CreatingATaskByCalendarId_StillWorksAndCreatesNothing()
    {
        var activityId = await SeedActivityAsync("lazy-by-id");
        var date = new DateOnly(2033, 4, 16);
        var calendarId = await SeedCalendarAsync(date);

        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/planner-task",
            TaskBody(activityId, 9, calendarId: calendarId), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = CreateDbContext();
        var rows = await db.Set<Calendar>()
            .IgnoreQueryFilters()
            .CountAsync(c => c.UserId == UserId && c.Date == date, CancellationToken);
        rows.Should().Be(1);
    }

    /// <summary>
    /// Moving a task onto an unplanned day creates that day too — otherwise a task dragged past the horizon
    /// fails on a foreign key, which is the same defect wearing a different endpoint.
    /// </summary>
    [Fact]
    public async Task MovingATaskToAnUnplannedDay_CreatesThatDay()
    {
        var activityId = await SeedActivityAsync("lazy-move");
        var from = new DateOnly(2033, 4, 17);
        var to = new DateOnly(2033, 4, 18);

        var created = await PostTaskAsync(activityId, from);
        var taskId = await created.Content.ReadFromJsonAsync<long>(JsonOpts, CancellationToken);

        await AssertNoCalendarAsync(to);

        var client = CreateClient();
        var response = await client.PutAsJsonAsync($"/api/planner-task/{taskId}",
            TaskBody(activityId, 9, date: to), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        (await FindCalendarAsync(to)).Should().NotBeNull();
        (await GetPlanAsync(to)).Tasks.Should().HaveCount(1);
        (await GetPlanAsync(from)).Tasks.Should().BeEmpty();
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static object TaskBody(long activityId, int hour, DateOnly? date = null, long? calendarId = null) => new
    {
        StartTime = new { Hours = hour, Minutes = 0 },
        EndTime = new { Hours = hour + 1, Minutes = 0 },
        IsBackground = false,
        ActivityId = activityId,
        Status = "NotStarted",
        Date = date,
        CalendarId = calendarId
    };

    private Task<HttpResponseMessage> PostTaskAsync(long activityId, DateOnly date, int hour = 9) =>
        CreateClient().PostAsJsonAsync("/api/planner-task", TaskBody(activityId, hour, date), JsonOpts, CancellationToken);

    private async Task<DayPlanResponse> GetPlanAsync(DateOnly date)
    {
        var response = await CreateClient().GetAsync($"/api/calendar/day-plan/{date:yyyy-MM-dd}", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<DayPlanResponse>(JsonOpts, CancellationToken))!;
    }

    private async Task<Calendar?> FindCalendarAsync(DateOnly date)
    {
        await using var db = CreateDbContext();
        return await db.Set<Calendar>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.UserId == UserId && c.Date == date, CancellationToken);
    }

    private async Task AssertNoCalendarAsync(DateOnly date) =>
        (await FindCalendarAsync(date)).Should().BeNull("the test is only meaningful on a date with no row");

    private async Task<long> SeedActivityAsync(string name)
    {
        await using var db = CreateDbContext();

        var role = new ActivityRole { UserId = UserId, Name = $"{name}-role", Color = "#123456" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity { UserId = UserId, Name = name, RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);
        return activity.Id;
    }

    private async Task<long> SeedDayTemplateAsync()
    {
        await using var db = CreateDbContext();

        var template = new TaskPlannerDayTemplate
        {
            UserId = UserId,
            Name = $"Template {Guid.NewGuid():N}",
            IsActive = true,
            SuggestedForDayType = DayType.Workday
        };
        db.Set<TaskPlannerDayTemplate>().Add(template);
        await db.SaveChangesAsync(CancellationToken);
        return template.Id;
    }

    private async Task<long> SeedCalendarAsync(DateOnly date)
    {
        await using var db = CreateDbContext();

        var existing = await db.Set<Calendar>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.UserId == UserId && c.Date == date, CancellationToken);
        if (existing != null)
            return existing.Id;

        var calendar = new Calendar
        {
            UserId = UserId,
            Date = date,
            DayType = DayType.Workday,
            WakeUpTime = new TimeOnly(7, 0),
            BedTime = new TimeOnly(23, 0)
        };
        db.Set<Calendar>().Add(calendar);
        await db.SaveChangesAsync(CancellationToken);
        return calendar.Id;
    }
}
