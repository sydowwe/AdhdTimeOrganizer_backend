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
/// The day-plan completion streak, end to end over the plan response that carries it.
/// <para>
/// The rules themselves are pinned without a database in
/// <see cref="Services.PlannerStreakServiceTests"/>. What is left for this file is everything that
/// <b>fails silently</b> around them: the streak not reaching the client at all (it hangs off
/// <c>CalendarResponse</c>, which every other calendar read leaves null, so a dropped line here is a null
/// field and not a build error), the qualifying-task predicate in <c>PlannerStreakReader</c> drifting from
/// the rules it feeds, and the un-tick path — the defect this feature exists to fix — not actually moving
/// the number when driven through the real status endpoint.
/// </para>
/// </summary>
[Collection("Postgres")]
public class PlannerStreakTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    /// <summary>
    /// Three days running, all complete. Also the smoke test for the ride-along itself — a streak that never
    /// reaches the response is not a null pointer anywhere, just a chip stuck on zero.
    /// </summary>
    [Fact]
    public async Task Streak_OfThreeCompleteDays_RidesAlongOnThePlanResponse()
    {
        var activityId = await SeedActivityAsync("streak-basic");
        var today = await ServerTodayAsync();

        foreach (var daysAgo in new[] { 2, 1, 0 })
            await SeedTaskAsync(activityId, today.AddDays(-daysAgo), PlannerTaskStatus.Completed);

        var streak = await GetStreakAsync(today);

        streak.CurrentStreak.Should().Be(3);
        streak.BestStreak.Should().Be(3);
        streak.IsTodayComplete.Should().BeTrue();
        streak.Today.Should().Be(today);
        streak.Timezone.Should().NotBeNullOrWhiteSpace("the server must say which day boundary it used");
    }

    /// <summary>
    /// A skipped task leaves the denominator, so the day still counts. Driven through the reader rather than
    /// the pure rules because the predicate that decides what a "qualifying" task is lives in the query, and
    /// a skip that never reached the Cancelled tally would look identical to an unfinished task.
    /// </summary>
    [Fact]
    public async Task Streak_DayWithASkippedTask_StillCounts()
    {
        var activityId = await SeedActivityAsync("streak-skip");
        var today = await ServerTodayAsync();

        await SeedTaskAsync(activityId, today, PlannerTaskStatus.Completed);
        await SeedTaskAsync(activityId, today, PlannerTaskStatus.Cancelled);

        var streak = await GetStreakAsync(today);

        streak.CurrentStreak.Should().Be(1);
        streak.IsTodayComplete.Should().BeTrue();
    }

    /// <summary>
    /// Skipping everything is not a perfect day. The day drops out as empty instead — it does not break the
    /// streak, but it does not extend it either.
    /// </summary>
    [Fact]
    public async Task Streak_DayWhereEverythingWasSkipped_DoesNotCount()
    {
        var activityId = await SeedActivityAsync("streak-all-skipped");
        var today = await ServerTodayAsync();

        await SeedTaskAsync(activityId, today.AddDays(-1), PlannerTaskStatus.Completed);
        await SeedTaskAsync(activityId, today, PlannerTaskStatus.Cancelled);
        await SeedTaskAsync(activityId, today, PlannerTaskStatus.Cancelled);

        var streak = await GetStreakAsync(today);

        streak.CurrentStreak.Should().Be(1, "yesterday still counts; an all-skipped today adds nothing");
        streak.IsTodayComplete.Should().BeFalse();
    }

    /// <summary>
    /// Background and optional tasks are excluded by the query, not by the rules — this is the only place
    /// that predicate is exercised. Note the consequence the frontend needs to know about: this day reads
    /// 1/3 on the widget's progress ring and is nonetheless complete here.
    /// </summary>
    [Fact]
    public async Task Streak_BackgroundAndOptionalTasks_CannotBlockTheDay()
    {
        var activityId = await SeedActivityAsync("streak-excluded");
        var today = await ServerTodayAsync();
        var optionalId = await SeedOptionalImportanceAsync();

        await SeedTaskAsync(activityId, today, PlannerTaskStatus.Completed);
        await SeedTaskAsync(activityId, today, PlannerTaskStatus.NotStarted, isBackground: true);
        await SeedTaskAsync(activityId, today, PlannerTaskStatus.NotStarted, importanceId: optionalId);

        var streak = await GetStreakAsync(today);

        streak.IsTodayComplete.Should().BeTrue();
        streak.CurrentStreak.Should().Be(1);
    }

    /// <summary>
    /// A day made of nothing but background tasks has an empty denominator, so it is invisible rather than
    /// failed — the guard against "excluded from the numerator only", which would leave such a day
    /// permanently incomplete and break every streak that crosses it.
    /// </summary>
    [Fact]
    public async Task Streak_DayOfOnlyBackgroundTasks_IsInvisible()
    {
        var activityId = await SeedActivityAsync("streak-background-only");
        var today = await ServerTodayAsync();

        await SeedTaskAsync(activityId, today.AddDays(-2), PlannerTaskStatus.Completed);
        await SeedTaskAsync(activityId, today.AddDays(-1), PlannerTaskStatus.NotStarted, isBackground: true);
        await SeedTaskAsync(activityId, today, PlannerTaskStatus.Completed);

        var streak = await GetStreakAsync(today);

        streak.CurrentStreak.Should().Be(2);
    }

    /// <summary>
    /// <b>The defect this feature exists to fix.</b> The localStorage store decremented its counter and rolled
    /// its stored date back exactly one day on an un-tick — a guess with no way to know whether the day before
    /// was also complete, so un-ticking and re-ticking silently corrupted the number on a single device.
    /// <para>
    /// Driven through the real <c>PATCH /planner-task/{id}/status</c> endpoint, because the point is that the
    /// write path needs to do nothing streak-specific at all: the number is derived from the rows the patch
    /// already changed, so it moves and comes back on its own.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Streak_UntickingATask_LowersIt_AndRetickingRestoresItExactly()
    {
        var activityId = await SeedActivityAsync("streak-untick");
        var today = await ServerTodayAsync();

        await SeedTaskAsync(activityId, today.AddDays(-2), PlannerTaskStatus.Completed);
        var yesterdayTaskId = await SeedTaskAsync(activityId, today.AddDays(-1), PlannerTaskStatus.Completed);
        await SeedTaskAsync(activityId, today, PlannerTaskStatus.Completed);

        (await GetStreakAsync(today)).CurrentStreak.Should().Be(3);

        await PatchStatusAsync(yesterdayTaskId, PlannerTaskStatus.NotStarted);

        var afterUntick = await GetStreakAsync(today);
        afterUntick.CurrentStreak.Should().Be(1, "yesterday now breaks the run, leaving only today");
        afterUntick.BestStreak.Should().Be(1, "the run that was 3 never actually happened once the tick is gone");

        await PatchStatusAsync(yesterdayTaskId, PlannerTaskStatus.Completed);

        (await GetStreakAsync(today)).CurrentStreak.Should().Be(3, "re-ticking restores the exact number");
    }

    /// <summary>
    /// An unfinished today does not zero the chip. Without this the number would read 0 every morning and
    /// climb back by evening, which is the most visible way for the feature to look broken.
    /// </summary>
    [Fact]
    public async Task Streak_WithTodayStillUnfinished_KeepsYesterdaysRun()
    {
        var activityId = await SeedActivityAsync("streak-today-open");
        var today = await ServerTodayAsync();

        await SeedTaskAsync(activityId, today.AddDays(-1), PlannerTaskStatus.Completed);
        await SeedTaskAsync(activityId, today, PlannerTaskStatus.InProgress);

        var streak = await GetStreakAsync(today);

        streak.CurrentStreak.Should().Be(1);
        streak.IsTodayComplete.Should().BeFalse();
    }

    /// <summary>
    /// The streak is a fact about the user, so opening a past day shows the same number — and
    /// <c>IsTodayComplete</c> stays about the real today, never about the day requested.
    /// </summary>
    [Fact]
    public async Task Streak_ReadFromAPastDay_IsTheSameUserLevelNumber()
    {
        var activityId = await SeedActivityAsync("streak-past-day");
        var today = await ServerTodayAsync();

        await SeedTaskAsync(activityId, today.AddDays(-1), PlannerTaskStatus.Completed);
        await SeedTaskAsync(activityId, today, PlannerTaskStatus.NotStarted);

        var fromToday = await GetStreakAsync(today);
        var fromYesterday = await GetStreakAsync(today.AddDays(-1));

        fromYesterday.CurrentStreak.Should().Be(fromToday.CurrentStreak);
        fromYesterday.IsTodayComplete.Should().BeFalse("IsTodayComplete is about today, not about the day requested");
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The day the <i>server</i> considers today, taken from the response rather than computed here. The
    /// boundary is the user's own timezone, so a test that seeded against its own clock would be flaky in
    /// exactly the window the feature is about.
    /// </summary>
    private async Task<DateOnly> ServerTodayAsync()
    {
        var probe = DateOnly.FromDateTime(DateTime.UtcNow);
        await SeedCalendarAsync(probe);
        var streak = await GetStreakAsync(probe);
        return streak.Today;
    }

    private async Task<PlannerStreakResponse> GetStreakAsync(DateOnly date)
    {
        var client = CreateClient();
        var response = await client.GetAsync($"/api/calendar/by-Date/{date:dd-MM-yyyy}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the plan response is where the streak rides");

        var body = await response.Content.ReadFromJsonAsync<CalendarResponse>(JsonOpts, CancellationToken);
        body!.Streak.Should().NotBeNull("the streak must reach the client on the plan response");
        return body.Streak!;
    }

    private async Task PatchStatusAsync(long taskId, PlannerTaskStatus status)
    {
        var client = CreateClient();
        var response = await client.PatchAsJsonAsync(
            $"/api/planner-task/{taskId}/status", new { Status = status.ToString() }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

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

    private async Task<long> SeedOptionalImportanceAsync()
    {
        await using var db = CreateDbContext();

        var importance = new TaskImportance
        {
            UserId = UserId,
            Text = "Optional",
            Importance = TaskImportance.OptionalMarkerValue
        };
        db.Set<TaskImportance>().Add(importance);
        await db.SaveChangesAsync(CancellationToken);
        return importance.Id;
    }

    private async Task<long> SeedCalendarAsync(DateOnly date)
    {
        await using var db = CreateDbContext();

        var calendar = await db.Set<Calendar>()
            .FirstOrDefaultAsync(c => c.UserId == UserId && c.Date == date, CancellationToken);

        if (calendar != null)
            return calendar.Id;

        calendar = new Calendar
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

    private async Task<long> SeedTaskAsync(
        long activityId,
        DateOnly date,
        PlannerTaskStatus status,
        bool isBackground = false,
        long? importanceId = null)
    {
        var calendarId = await SeedCalendarAsync(date);

        await using var db = CreateDbContext();

        var task = new PlannerTask
        {
            UserId = UserId,
            ActivityId = activityId,
            CalendarId = calendarId,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
            IsBackground = isBackground,
            ImportanceId = importanceId,
            Status = status
        };
        db.Set<PlannerTask>().Add(task);
        await db.SaveChangesAsync(CancellationToken);
        return task.Id;
    }
}
