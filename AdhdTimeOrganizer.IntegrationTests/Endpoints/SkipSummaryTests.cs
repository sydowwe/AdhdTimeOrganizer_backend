using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The range skip summary, end to end over <c>POST /planner-task/skip-summary</c> (B1).
/// <para>
/// The grouping is pinned without a database in <see cref="Services.SkipSummaryServiceTests"/>. What is left
/// here is the half that lives in the <b>query</b> and fails silently: background and today leave the
/// population before the rules see a row, Cancelled does <b>not</b> (the opposite of the trend), the user
/// scoping is applied by hand, and an un-skip leaves the next read with no repair path. The range 400s are
/// pinned with the other range routes in <see cref="PlanVsActualTrendTests"/>.
/// </para>
/// <para>
/// The caller is pinned to <b>UTC</b> so "today" is the same day the test computes, as in the trend tests.
/// </para>
/// </summary>
[Collection("Postgres")]
public class SkipSummaryTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private const string Route = "/api/planner-task/skip-summary";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    protected override async Task SeedAsync(DbContext db)
    {
        var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == UserId);
        user.Timezone = TimeZoneInfo.Utc;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The client's verification step 1, skipped through the app's own status route. The two built-in labels
    /// sent as text — one per locale — are stored as one key and come back as one key group. A near-miss casing
    /// stays exact text (folding is the client's, in one place), and a reasonless skip is its own group, so the
    /// counts add up to <c>skippedCount</c>. Untouched tasks are not skips.
    /// </summary>
    [Fact]
    public async Task Summary_GroupsBuiltInLabelsByKey_AndTheRestByExactText()
    {
        var activityId = await SeedActivityAsync("skip-reasons");
        var dayA = Today.AddDays(-3);
        var dayB = Today.AddDays(-2);

        var en = await SeedTaskAsync(activityId, dayA, PlannerTaskStatus.NotStarted, "09:00");
        var sk = await SeedTaskAsync(activityId, dayA, PlannerTaskStatus.NotStarted, "10:00");
        var lower = await SeedTaskAsync(activityId, dayB, PlannerTaskStatus.NotStarted, "09:00");
        var none = await SeedTaskAsync(activityId, dayB, PlannerTaskStatus.NotStarted, "11:00");
        await SeedTaskAsync(activityId, dayB, PlannerTaskStatus.NotStarted, "12:00");
        await SeedTaskAsync(activityId, dayB, PlannerTaskStatus.Completed, "13:00");

        await SkipAsync(en, new { Status = "Cancelled", SkipReason = "Ran out of time" });
        await SkipAsync(sk, new { Status = "Cancelled", SkipReason = "Nestihol som" });
        await SkipAsync(lower, new { Status = "Cancelled", SkipReason = "ran out of time" });
        await SkipAsync(none, new { Status = "Cancelled" });

        var summary = await GetSummaryAsync(Today.AddDays(-7), Today.AddDays(-1));

        summary.SkippedCount.Should().Be(4);
        summary.Reasons.Should().BeEquivalentTo(new[]
        {
            new SkipReasonCountResponse { Reason = null, ReasonKey = "noTime", Count = 2 },
            new SkipReasonCountResponse { Reason = "ran out of time", ReasonKey = null, Count = 1 },
            new SkipReasonCountResponse { Reason = null, ReasonKey = null, Count = 1 }
        });
    }

    /// <summary>A skip sent with a key groups with the same label sent as text — one reason, however it arrived.</summary>
    [Fact]
    public async Task Summary_KeyAndItsLabelAsText_AreOneGroup()
    {
        var activityId = await SeedActivityAsync("skip-keys");
        var day = Today.AddDays(-1);

        var byKey = await SeedTaskAsync(activityId, day, PlannerTaskStatus.NotStarted, "09:00");
        var byLabel = await SeedTaskAsync(activityId, day, PlannerTaskStatus.NotStarted, "10:00");

        await SkipAsync(byKey, new { Status = "Cancelled", SkipReasonKey = "noEnergy" });
        await SkipAsync(byLabel, new { Status = "Cancelled", SkipReason = "Nemal som energiu" });

        var summary = await GetSummaryAsync(day, day);

        summary.Reasons.Should().ContainSingle().Which.Should()
            .BeEquivalentTo(new SkipReasonCountResponse { Reason = null, ReasonKey = "noEnergy", Count = 2 });
    }

    /// <summary>
    /// The per-hour base rate. Its denominator is every non-background task in any status — Cancelled and
    /// Completed included — which is exactly what the trend's population leaves out, and why this is a sibling
    /// endpoint rather than more fields on the trend.
    /// </summary>
    [Fact]
    public async Task Summary_ByStartHour_CountsEveryPlannedTaskAsTheDenominator()
    {
        var activityId = await SeedActivityAsync("skip-hours");
        var dayA = Today.AddDays(-3);
        var dayB = Today.AddDays(-2);

        // Evening: six planned across two days, four skipped.
        await SeedTaskAsync(activityId, dayA, PlannerTaskStatus.Cancelled, "20:00", "Too tired");
        await SeedTaskAsync(activityId, dayA, PlannerTaskStatus.Cancelled, "20:30");
        await SeedTaskAsync(activityId, dayA, PlannerTaskStatus.Completed, "21:00");
        await SeedTaskAsync(activityId, dayB, PlannerTaskStatus.Cancelled, "21:00", "Too tired");
        await SeedTaskAsync(activityId, dayB, PlannerTaskStatus.Cancelled, "21:30", "Too tired");
        await SeedTaskAsync(activityId, dayB, PlannerTaskStatus.NotStarted, "21:45");
        // Morning: three planned, none skipped.
        await SeedTaskAsync(activityId, dayA, PlannerTaskStatus.Completed, "09:00");
        await SeedTaskAsync(activityId, dayB, PlannerTaskStatus.Completed, "09:15");
        await SeedTaskAsync(activityId, dayB, PlannerTaskStatus.NotStarted, "10:00");

        var summary = await GetSummaryAsync(dayA, dayB);

        summary.SkippedCount.Should().Be(4);
        summary.ByStartHour.Should().BeEquivalentTo(new[]
        {
            new SkipHourCountResponse { Hour = 9, PlannedCount = 2, SkippedCount = 0 },
            new SkipHourCountResponse { Hour = 10, PlannedCount = 1, SkippedCount = 0 },
            new SkipHourCountResponse { Hour = 20, PlannedCount = 2, SkippedCount = 2 },
            new SkipHourCountResponse { Hour = 21, PlannedCount = 4, SkippedCount = 2 }
        }, o => o.WithStrictOrdering());
    }

    /// <summary>
    /// Today is excluded even though a skip made today is already final — today's not-yet-reached tasks would
    /// inflate the denominator, and this line must cover the same days as the trend beside it.
    /// </summary>
    [Fact]
    public async Task Summary_ExcludesToday_EvenAFinalSkip()
    {
        var activityId = await SeedActivityAsync("skip-today");

        await SeedTaskAsync(activityId, Today.AddDays(-1), PlannerTaskStatus.Cancelled, "09:00", "Meeting ran long");
        await SeedTaskAsync(activityId, Today, PlannerTaskStatus.Cancelled, "09:00", "Meeting ran long");
        await SeedTaskAsync(activityId, Today, PlannerTaskStatus.NotStarted, "10:00");

        var acrossToday = await GetSummaryAsync(Today.AddDays(-7), Today);

        acrossToday.SkippedCount.Should().Be(1, "only yesterday's skip is on a day that is over");
        acrossToday.ByStartHour.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new SkipHourCountResponse { Hour = 9, PlannedCount = 1, SkippedCount = 1 });

        var todayOnly = await GetSummaryAsync(Today, Today);

        todayOnly.SkippedCount.Should().Be(0);
        todayOnly.ByStartHour.Should().BeEmpty();
    }

    /// <summary>A skipped background task is in neither the skips nor the per-hour denominator.</summary>
    [Fact]
    public async Task Summary_BackgroundTasks_LeaveThePopulationEntirely()
    {
        var activityId = await SeedActivityAsync("skip-background");
        var day = Today.AddDays(-1);

        await SeedTaskAsync(activityId, day, PlannerTaskStatus.Cancelled, "09:00", "Meeting ran long", isBackground: true);
        await SeedTaskAsync(activityId, day, PlannerTaskStatus.NotStarted, "09:30", isBackground: true);
        await SeedTaskAsync(activityId, day, PlannerTaskStatus.Completed, "14:00");

        var summary = await GetSummaryAsync(day, day);

        summary.SkippedCount.Should().Be(0);
        summary.Reasons.Should().BeEmpty();
        summary.ByStartHour.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new SkipHourCountResponse { Hour = 14, PlannedCount = 1, SkippedCount = 0 });
    }

    /// <summary>A range entirely in the future is a 200 with zeroes and empty lists — the client renders nothing.</summary>
    [Fact]
    public async Task Summary_RangeEntirelyInTheFuture_IsEmpty()
    {
        var summary = await GetSummaryAsync(Today.AddDays(1), Today.AddDays(30));

        summary.Should().BeEquivalentTo(new SkipSummaryResponse { SkippedCount = 0, Reasons = [], ByStartHour = [] });
    }

    /// <summary>
    /// The slice cannot see the host's global user filter, so the scoping is by hand — and a miss is silent: the
    /// numbers only come back bigger.
    /// </summary>
    [Fact]
    public async Task Summary_AnotherUsersSkips_AreNeverCounted()
    {
        var day = Today.AddDays(-1);
        var otherUserId = await CreateUserAsync("skip-summary-other@example.com");
        var mine = await SeedActivityAsync("skip-mine");
        var theirs = await SeedActivityAsync("skip-theirs", otherUserId);

        await SeedTaskAsync(mine, day, PlannerTaskStatus.Cancelled, "09:00", "Mine");
        await SeedTaskAsync(theirs, day, PlannerTaskStatus.Cancelled, "09:00", "Theirs", userId: otherUserId);
        await SeedTaskAsync(theirs, day, PlannerTaskStatus.Completed, "10:00", userId: otherUserId);

        var summary = await GetSummaryAsync(day, day);

        summary.SkippedCount.Should().Be(1);
        summary.Reasons.Should().ContainSingle().Which.Reason.Should().Be("Mine");
        summary.ByStartHour.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new SkipHourCountResponse { Hour = 9, PlannedCount = 1, SkippedCount = 1 });
    }

    /// <summary>
    /// Derived on every read: un-skipping a task through the app's own status route drops it from the next
    /// summary, with no repair path. The task stays planned, so the hour's denominator does not move.
    /// </summary>
    [Fact]
    public async Task Summary_UnskippedTask_LeavesTheNextRead()
    {
        var activityId = await SeedActivityAsync("skip-unskip");
        var day = Today.AddDays(-2);

        var taskId = await SeedTaskAsync(activityId, day, PlannerTaskStatus.Cancelled, "09:00", "Meeting ran long");
        await SeedTaskAsync(activityId, day, PlannerTaskStatus.Cancelled, "09:30", "Meeting ran long");

        (await GetSummaryAsync(day, day)).SkippedCount.Should().Be(2);

        var patched = await CreateClient().PatchAsJsonAsync(
            $"api/planner-task/{taskId}/status", new { Status = "Completed" }, JsonOpts, CancellationToken);
        patched.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await GetSummaryAsync(day, day);

        after.SkippedCount.Should().Be(1);
        after.Reasons.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new SkipReasonCountResponse { Reason = "Meeting ran long", ReasonKey = null, Count = 1 });
        after.ByStartHour.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new SkipHourCountResponse { Hour = 9, PlannedCount = 2, SkippedCount = 1 });
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private async Task<SkipSummaryResponse> GetSummaryAsync(DateOnly from, DateOnly until)
    {
        var response = await CreateClient().PostAsJsonAsync(Route, new { from, until }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<SkipSummaryResponse>(JsonOpts, CancellationToken);
        body.Should().NotBeNull();
        return body!;
    }

    private async Task SkipAsync(long taskId, object body)
    {
        var response = await CreateClient().PatchAsJsonAsync($"api/planner-task/{taskId}/status", body, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private async Task<long> SeedActivityAsync(string name, long userId = UserId)
    {
        await using var db = CreateDbContext();
        return await PlanningTestSeedHelper.SeedActivityAsync(db, name, userId, CancellationToken);
    }

    private async Task<long> SeedTaskAsync(
        long activityId,
        DateOnly date,
        PlannerTaskStatus status,
        string start,
        string? skipReason = null,
        bool isBackground = false,
        long userId = UserId)
    {
        long calendarId;
        await using (var calendarDb = CreateDbContext())
            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(calendarDb, date, userId, CancellationToken);

        await using var db = CreateDbContext();

        var startTime = TimeOnly.ParseExact(start, "HH:mm", CultureInfo.InvariantCulture);
        var task = new PlannerTask
        {
            UserId = userId,
            ActivityId = activityId,
            CalendarId = calendarId,
            StartTime = startTime,
            EndTime = startTime.AddMinutes(30),
            IsBackground = isBackground,
            Status = status,
            SkipReason = skipReason
        };
        db.Set<PlannerTask>().Add(task);
        await db.SaveChangesAsync(CancellationToken);
        return task.Id;
    }

    /// <summary>A second real account — <c>PlannerTask.UserId</c> is a foreign key, so a bare id will not do.</summary>
    private async Task<long> CreateUserAsync(string email)
    {
        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
            return existing.Id;

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
        var result = await userManager.CreateAsync(user, "Str0ng!Passw0rd");
        result.Succeeded.Should().BeTrue();
        return user.Id;
    }
}
