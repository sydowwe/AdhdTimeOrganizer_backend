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
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    // Christmas Day 2034 — a Monday. There is no Holiday member in DayType (DOC-2), and
    // CalendarDayFactory:35-37 states the rule: "a holiday on a Tuesday is still a Workday row with a
    // HolidayName." A day type derived from the holiday rather than the weekday would fail here.
    [InlineData(2034, 12, 25, DayType.Workday, "Prvý sviatok vianočný")]
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

    // ─── the provisioner's race branch ───────────────────────────────────────

    /// <summary>
    /// Two devices starting the same morning's first task on a day nobody has planned. <c>(user_id, date)</c>
    /// is unique, so one of the two inserts loses — and the loser must still land its task, because a race it
    /// never knew it was in is not something the user can act on.
    /// <para>
    /// This is the endpoint-level shape of
    /// <see cref="TheLoserOfTheCalendarRace_AdoptsTheWinnersRow_AndStagesNothingOnTheContext"/>, which forces
    /// the same branch deterministically. Two requests may serialise and never actually collide, in which case
    /// this passes vacuously rather than flaking — it is a regression pin, repeated over several dates to
    /// raise the odds, and the deterministic test is the proof.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(2029, 6, 14)]
    [InlineData(2029, 6, 15)]
    [InlineData(2029, 6, 16)]
    public async Task TwoConcurrentTasksOnAnUnplannedDay_BothLand_OnOneSharedDay(int year, int month, int day)
    {
        var activityId = await SeedActivityAsync($"lazy-race-{year}-{month}-{day}");
        var date = new DateOnly(year, month, day);

        await AssertNoCalendarAsync(date);

        var responses = await Task.WhenAll(new[] { 8, 10 }.Select(hour =>
            CreateUserRoleClient().PostAsJsonAsync(
                "api/planner-task", TaskBody(activityId, hour, date), JsonOpts, CancellationToken)));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created,
            "the loser reads the existing row rather than failing a write the user has every right to make");

        await using var verify = CreateDbContext();
        var calendars = await verify.Set<Calendar>().IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.UserId == UserId && c.Date == date)
            .ToListAsync(CancellationToken);
        calendars.Should().ContainSingle("(user_id, date) is unique — one day, however many writers");

        var tasks = await verify.Set<PlannerTask>().IgnoreQueryFilters().AsNoTracking()
            .CountAsync(t => t.CalendarId == calendars.Single().Id, CancellationToken);
        tasks.Should().Be(2,
            "both tasks hang off the one day that survived; drop the detach and the loser's own task goes down "
            + "with the calendar insert its request re-sends");
    }

    /// <summary>
    /// The detach in <c>CalendarProvisioner</c>'s <c>DbUpdateException</c> branch, forced rather than raced:
    /// the winner's row is committed from a second context, so the provisioner's read misses it and its insert
    /// is the one that loses.
    /// <para>
    /// The assertion that matters is the last one. A failed insert stays <c>Added</c> on the change tracker,
    /// so without the detach the <i>caller's</i> next <c>SaveChangesAsync</c> — the endpoint's own write, not
    /// anything the provisioner does — re-sends the duplicate calendar and takes the user's task down with it.
    /// Nothing about this method's return value shows that, which is why every single-writer test above misses
    /// it.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "The loser of the calendar race adopts the winner's row and stages nothing on the context")]
    public async Task TheLoserOfTheCalendarRace_AdoptsTheWinnersRow_AndStagesNothingOnTheContext()
    {
        var activityId = await SeedActivityAsync("lazy-race-detach");
        var date = new DateOnly(2029, 7, 2);

        await using var scope = Fixture.AdminAndUserFactory.Services.CreateAsyncScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<ICalendarProvisioner>();
        var db = scope.ServiceProvider.GetRequiredService<DbContext>();

        // The winner, committed from a context this scope knows nothing about.
        long winnerId;
        await using (var other = CreateDbContext())
            winnerId = await PlanningTestSeedHelper.SeedCalendarAsync(other, date, UserId, CancellationToken);

        var resolved = await provisioner.EnsureForDateAsync(UserId, date, CancellationToken);

        resolved.Id.Should().Be(winnerId, "the loser adopts the row that exists instead of failing");

        db.ChangeTracker.Entries<Calendar>().Should().NotContain(e => e.State == EntityState.Added,
            "the failed insert has to be detached or it is still staged for whatever saves next");

        // The caller's own write, on the caller's own context — the part the detach actually protects.
        db.Set<PlannerTask>().Add(new PlannerTask
        {
            UserId = UserId,
            ActivityId = activityId,
            CalendarId = winnerId,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
            IsBackground = false,
            Status = PlannerTaskStatus.NotStarted
        });
        await db.SaveChangesAsync(CancellationToken);

        await using var verify = CreateDbContext();
        (await verify.Set<Calendar>().IgnoreQueryFilters()
            .CountAsync(c => c.UserId == UserId && c.Date == date, CancellationToken))
            .Should().Be(1);
        (await verify.Set<PlannerTask>().IgnoreQueryFilters()
            .CountAsync(t => t.CalendarId == winnerId, CancellationToken))
            .Should().Be(1, "losing the calendar race costs the request nothing but a wasted insert");
    }

    // ─── acting for a user who is not the signed-in one ──────────────────────

    /// <summary>
    /// <c>FindAsync</c> ignores the query filter and scopes by hand, deliberately: the provisioner is *told*
    /// which user to act for. Filtered by whoever happens to be signed in, the read would find nothing for
    /// anyone else and try to insert a duplicate onto <c>(user_id, date)</c> — a 500 on a row that already
    /// exists.
    /// </summary>
    [Fact(DisplayName = "Provisioning for another user finds that user's existing day, not nothing")]
    public async Task ProvisioningForAnotherUser_ReadsTheirExistingDay()
    {
        var date = new DateOnly(2029, 7, 9);

        long otherUserId, theirCalendarId;
        await using (var db = CreateDbContext())
        {
            otherUserId = (await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken)).Id;
            theirCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, date, otherUserId, CancellationToken);
        }

        // The ambient logged user in this scope is the test user, not the one being acted for.
        await using var scope = Fixture.AdminAndUserFactory.Services.CreateAsyncScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<ICalendarProvisioner>();

        var resolved = await provisioner.EnsureForDateAsync(otherUserId, date, CancellationToken);

        resolved.Id.Should().Be(theirCalendarId);

        await using var verify = CreateDbContext();
        (await verify.Set<Calendar>().IgnoreQueryFilters()
            .CountAsync(c => c.UserId == otherUserId && c.Date == date, CancellationToken))
            .Should().Be(1, "their day already existed; a filtered read would have inserted a second one");
        (await verify.Set<Calendar>().IgnoreQueryFilters()
            .CountAsync(c => c.UserId == UserId && c.Date == date, CancellationToken))
            .Should().Be(0, "acting for someone else must not mint a day for the signed-in user");
    }

    /// <summary>
    /// The mirror, and the control for the test above: with no row to find, the day it creates is owned by the
    /// user it was told to act for — not by the one holding the scope.
    /// </summary>
    [Fact(DisplayName = "Provisioning for another user creates the day owned by them, not by the signed-in user")]
    public async Task ProvisioningForAnotherUser_WithNoRow_CreatesOneOwnedByThem()
    {
        var date = new DateOnly(2029, 7, 10);

        long otherUserId;
        await using (var db = CreateDbContext())
            otherUserId = (await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken)).Id;

        await using var scope = Fixture.AdminAndUserFactory.Services.CreateAsyncScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<ICalendarProvisioner>();

        var created = await provisioner.EnsureForDateAsync(otherUserId, date, CancellationToken);

        created.UserId.Should().Be(otherUserId);

        await using var verify = CreateDbContext();
        var rows = await verify.Set<Calendar>().IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Date == date)
            .ToListAsync(CancellationToken);
        rows.Should().ContainSingle("exactly one day, and only for the user it was asked about");
        rows.Single().UserId.Should().Be(otherUserId);
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
