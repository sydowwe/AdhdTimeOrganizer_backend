using AdhdTimeOrganizer.Planning.application.notification;
using AdhdTimeOrganizer.History.domain.model.@enum;
using System.Security.Claims;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.History.domain.model.entity.timerAlarm;
using AdhdTimeOrganizer.History.infrastructure.scheduling;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using AdhdTimeOrganizer.Planning.infrastructure.jobs;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.application;
using Sydowwe.Notifications.domain.entity;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// Audit N5 — <see cref="PlannerSlipNudgeJobHandler"/>, the sweep that says "2 blocks slipped — re-plan the rest of
/// today?" and "time's up for X — wrap up or extend?". It runs with nobody watching and its every mistake is silent (a
/// nudge too many is a reproach, one too few is nothing at all), so every case asserts on what the notifier was handed
/// and on the marks left on the rows.
/// <para>
/// <b>The clock is pinned to Tuesday 29 September 2020</b>, for the reason <c>RoutineSuggestedTodayJobTests</c> gives:
/// Quartz runs this job every five minutes inside every test host with the <i>real</i> clock, and only a date no real
/// clock will produce keeps those fires off the rows under test. For the same reason every seeded block is backdated
/// to having been created long before its slot — the sweep ignores a block created after its own end.
/// </para>
/// </summary>
[Collection("Postgres")]
public class PlannerSlipNudgeJobTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private static readonly DateOnly Tuesday = new(2020, 9, 29);

    private static DateTimeOffset At(int hour, int minute = 0, DateOnly? date = null) =>
        new((date ?? Tuesday).ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero);

    // ---- test doubles -----------------------------------------------------------------------------

    private sealed class NotifierDouble : IPlannerNotificationService
    {
        public sealed record MissedCall(long UserId, DateOnly Date, int Count, long FirstTaskId);

        public sealed record OverrunCall(long UserId, long PlannerTaskId, string? ActivityName, int MinutesOver);

        public List<MissedCall> Missed { get; } = [];
        public List<OverrunCall> Overruns { get; } = [];

        /// <summary>Mirrors the real implementation swallowing a dispatch failure: recorded, and reported as not sent.</summary>
        public bool DispatchFails { get; set; }

        public Task<bool> NotifyBlocksMissedAsync(long userId, DateOnly date, int count, long firstTaskId, CancellationToken ct = default)
        {
            Missed.Add(new MissedCall(userId, date, count, firstTaskId));
            return Task.FromResult(!DispatchFails);
        }

        public Task<bool> NotifyBlockOverrunAsync(long userId, long plannerTaskId, string? activityName, int minutesOver, CancellationToken ct = default)
        {
            Overruns.Add(new OverrunCall(userId, plannerTaskId, activityName, minutesOver));
            return Task.FromResult(!DispatchFails);
        }
    }

    /// <summary>The real background scope has no user. See <c>RoutineSuggestedTodayJobTests</c> for why the fake user cannot stand in.</summary>
    private sealed class NoAmbientUserLoggedUserService : ILoggedUserService
    {
        public ClaimsPrincipal? LoggedUserPrincipal => null;
        public bool IsAuthenticated => false;
        public IEnumerable<string> GetRoles => [];
        public string GetEmail => throw new InvalidOperationException("No ambient user in a background job scope.");
        public long GetUserId => throw new InvalidOperationException("No ambient user in a background job scope.");
        public long? GetUserIdOrNull => null;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // ---- missed -----------------------------------------------------------------------------------

    [Fact(DisplayName = "N5: a missed block sends one notification and is marked; the next run sends nothing")]
    public async Task MissedBlock_IsAnnouncedOnce()
    {
        var blockId = await SeedBlockAsync(UserId, Tuesday, new TimeOnly(9, 0), new TimeOnly(10, 0));

        var notifier = new NotifierDouble();
        await RunJobAsync(At(10, 5), notifier);

        notifier.Missed.Should().ContainSingle().Which.Should().Be(new NotifierDouble.MissedCall(UserId, Tuesday, 1, blockId));
        (await ReadMarksAsync(blockId)).Missed.Should().Be(At(10, 5).UtcDateTime);

        await RunJobAsync(At(10, 10), notifier);
        notifier.Missed.Should().ContainSingle("the block was already announced for this slot");
    }

    /// <summary>
    /// Three slipped blocks are one notification, not three — and it counts only what is missed: a block still running
    /// late, one still to come, one done, one cancelled and a background one are all left out.
    /// </summary>
    [Fact(DisplayName = "N5: three missed blocks in one run are one notification with Count = 3, linked to the earliest")]
    public async Task ThreeMissedBlocks_AreOneNotification()
    {
        var earliest = await SeedBlockAsync(UserId, Tuesday, new TimeOnly(8, 0), new TimeOnly(9, 0));
        var second = await SeedBlockAsync(UserId, Tuesday, new TimeOnly(9, 0), new TimeOnly(10, 0));
        var third = await SeedBlockAsync(UserId, Tuesday, new TimeOnly(10, 0), new TimeOnly(11, 0));
        var late = await SeedBlockAsync(UserId, Tuesday, new TimeOnly(11, 30), new TimeOnly(12, 30));
        await SeedBlockAsync(UserId, Tuesday, new TimeOnly(13, 0), new TimeOnly(14, 0));
        await SeedBlockAsync(UserId, Tuesday, new TimeOnly(7, 0), new TimeOnly(8, 0), PlannerTaskStatus.Completed);
        await SeedBlockAsync(UserId, Tuesday, new TimeOnly(7, 0), new TimeOnly(8, 0), PlannerTaskStatus.Cancelled);
        await SeedBlockAsync(UserId, Tuesday, new TimeOnly(7, 0), new TimeOnly(8, 0), background: true);

        var notifier = new NotifierDouble();
        await RunJobAsync(At(12, 0), notifier);

        notifier.Missed.Should().ContainSingle().Which.Should().Be(new NotifierDouble.MissedCall(UserId, Tuesday, 3, earliest));
        foreach (var id in new[] { earliest, second, third })
            (await ReadMarksAsync(id)).Missed.Should().Be(At(12, 0).UtcDateTime, "every block in the notification shares its stamp");
        (await ReadMarksAsync(late)).Missed.Should().BeNull("a block still inside its slot is late, not missed");
    }

    [Fact(DisplayName = "N5: at most two missed notifications a day; later slips are not announced and stay unmarked")]
    public async Task DailyCap_HoldsAcrossRuns()
    {
        await SeedBlockAsync(UserId, Tuesday, new TimeOnly(8, 0), new TimeOnly(9, 0));
        await SeedBlockAsync(UserId, Tuesday, new TimeOnly(10, 0), new TimeOnly(11, 0));
        var third = await SeedBlockAsync(UserId, Tuesday, new TimeOnly(12, 0), new TimeOnly(13, 0));

        var notifier = new NotifierDouble();
        await RunJobAsync(At(9, 5), notifier);
        await RunJobAsync(At(11, 5), notifier);
        await RunJobAsync(At(13, 5), notifier);
        await RunJobAsync(At(13, 10), notifier);

        notifier.Missed.Should().HaveCount(PlannerSlipNudgeJobHandler.MissedNotificationsPerDay);
        (await ReadMarksAsync(third)).Missed.Should().BeNull("nothing went out for it");

        // The cap is per day: Wednesday starts afresh.
        await SeedBlockAsync(UserId, Tuesday.AddDays(1), new TimeOnly(8, 0), new TimeOnly(9, 0));
        await RunJobAsync(At(9, 5, Tuesday.AddDays(1)), notifier);
        notifier.Missed.Should().HaveCount(3).And.Subject.Last().Date.Should().Be(Tuesday.AddDays(1));
    }

    [Fact(DisplayName = "N5: a block moved to a later slot after its announcement can slip, and be announced, again")]
    public async Task MovedBlock_IsAnnouncedForItsNewSlot()
    {
        var blockId = await SeedBlockAsync(UserId, Tuesday, new TimeOnly(9, 0), new TimeOnly(10, 0));

        var notifier = new NotifierDouble();
        await RunJobAsync(At(10, 5), notifier);

        await using (var db = CreateDbContext())
            await db.Set<PlannerTask>().Where(t => t.Id == blockId).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.StartTime, new TimeOnly(11, 0))
                .SetProperty(t => t.EndTime, new TimeOnly(12, 0)), CancellationToken);

        await RunJobAsync(At(11, 30), notifier);
        notifier.Missed.Should().ContainSingle("the new slot has not ended yet");

        await RunJobAsync(At(12, 5), notifier);
        notifier.Missed.Should().HaveCount(2);
        (await ReadMarksAsync(blockId)).Missed.Should().Be(At(12, 5).UtcDateTime);
    }

    [Theory(DisplayName = "N5: nothing is announced on an away day")]
    [InlineData(DayType.Vacation)]
    [InlineData(DayType.SickDay)]
    public async Task AwayDay_AnnouncesNothing(DayType dayType)
    {
        await SeedBlockAsync(UserId, Tuesday, new TimeOnly(9, 0), new TimeOnly(10, 0), dayType: dayType);

        var notifier = new NotifierDouble();
        await RunJobAsync(At(10, 5), notifier);

        notifier.Missed.Should().BeEmpty();
    }

    /// <summary>A day template applied at noon puts the morning's blocks behind the user the moment they exist.</summary>
    [Fact(DisplayName = "N5: a block created after its own end was never an attempt and is not announced")]
    public async Task BlockCreatedAfterItsEnd_IsNotMissed()
    {
        await SeedBlockAsync(UserId, Tuesday, new TimeOnly(9, 0), new TimeOnly(10, 0), createdAt: At(10, 30).UtcDateTime);

        var notifier = new NotifierDouble();
        await RunJobAsync(At(11, 0), notifier);

        notifier.Missed.Should().BeEmpty();
    }

    [Fact(DisplayName = "N5: a swallowed dispatch leaves the blocks unmarked, and the next run retries")]
    public async Task SwallowedDispatch_IsRetried()
    {
        var blockId = await SeedBlockAsync(UserId, Tuesday, new TimeOnly(9, 0), new TimeOnly(10, 0));

        await RunJobAsync(At(10, 5), new NotifierDouble { DispatchFails = true });
        (await ReadMarksAsync(blockId)).Missed.Should().BeNull();

        var healthy = new NotifierDouble();
        await RunJobAsync(At(10, 10), healthy);
        healthy.Missed.Should().ContainSingle();
        (await ReadMarksAsync(blockId)).Missed.Should().Be(At(10, 10).UtcDateTime);
    }

    /// <summary>
    /// At 23:30 UTC on Tuesday it is Wednesday 08:30 in Tokyo. Each user is judged on their own today: the Tokyo user's
    /// Wednesday 07:00 block is missed and their Tuesday evening one is yesterday's; the UTC user's Tuesday 22:00 block is
    /// missed and their Wednesday 07:00 one has not come yet.
    /// </summary>
    [Fact(DisplayName = "N5: 'today' and 'missed' are on each user's own clock")]
    public async Task EachUser_IsJudgedOnTheirOwnClock()
    {
        const long tokyoUser = UserId + 40;
        await using (var db = CreateDbContext())
            await EnsureUserAsync(db, tokyoUser, TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"), CancellationToken);

        var wednesday = Tuesday.AddDays(1);
        var tokyoMissed = await SeedBlockAsync(tokyoUser, wednesday, new TimeOnly(7, 0), new TimeOnly(8, 0));
        await SeedBlockAsync(tokyoUser, Tuesday, new TimeOnly(20, 0), new TimeOnly(21, 0));
        var utcMissed = await SeedBlockAsync(UserId, Tuesday, new TimeOnly(22, 0), new TimeOnly(23, 0));
        await SeedBlockAsync(UserId, wednesday, new TimeOnly(7, 0), new TimeOnly(8, 0));

        var notifier = new NotifierDouble();
        await RunJobAsync(At(23, 30), notifier);

        notifier.Missed.Should().BeEquivalentTo([
            new NotifierDouble.MissedCall(tokyoUser, wednesday, 1, tokyoMissed),
            new NotifierDouble.MissedCall(UserId, Tuesday, 1, utcMissed)
        ]);
    }

    // ---- overrun ----------------------------------------------------------------------------------

    [Fact(DisplayName = "N5: a started block five minutes past its end is announced once, with how far over it is")]
    public async Task Overrun_IsAnnouncedOnce_AfterTheGrace()
    {
        var activity = $"Deep work {Guid.NewGuid():N}";
        var blockId = await SeedBlockAsync(UserId, Tuesday, new TimeOnly(12, 0), new TimeOnly(13, 0), PlannerTaskStatus.InProgress, activityName: activity);

        var notifier = new NotifierDouble();
        await RunJobAsync(At(13, 3), notifier);
        notifier.Overruns.Should().BeEmpty("three minutes over is inside the grace");

        await RunJobAsync(At(13, 10), notifier);
        notifier.Overruns.Should().ContainSingle().Which.Should().Be(new NotifierDouble.OverrunCall(UserId, blockId, activity, 10));
        (await ReadMarksAsync(blockId)).Overrun.Should().Be(At(13, 10).UtcDateTime);

        await RunJobAsync(At(13, 15), notifier);
        notifier.Overruns.Should().ContainSingle();
        notifier.Missed.Should().BeEmpty("a started block is never missed");
    }

    [Fact(DisplayName = "N5: an extended block can overrun, and be announced, again")]
    public async Task ExtendedBlock_IsAnnouncedForItsNewEnd()
    {
        var blockId = await SeedBlockAsync(UserId, Tuesday, new TimeOnly(12, 0), new TimeOnly(13, 0), PlannerTaskStatus.InProgress);

        var notifier = new NotifierDouble();
        await RunJobAsync(At(13, 10), notifier);

        await using (var db = CreateDbContext())
            await db.Set<PlannerTask>().Where(t => t.Id == blockId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.EndTime, new TimeOnly(13, 30)), CancellationToken);

        await RunJobAsync(At(13, 30), notifier);
        notifier.Overruns.Should().ContainSingle("the extension has not run out yet");

        await RunJobAsync(At(13, 35), notifier);
        notifier.Overruns.Should().HaveCount(2);
    }

    [Fact(DisplayName = "N5: an auto-complete block is the other job's, and never announced as an overrun")]
    public async Task AutoCompleteBlock_IsNotAnnounced()
    {
        await SeedBlockAsync(UserId, Tuesday, new TimeOnly(12, 0), new TimeOnly(13, 0), PlannerTaskStatus.InProgress, autoComplete: true);

        var notifier = new NotifierDouble();
        await RunJobAsync(At(13, 30), notifier);

        notifier.Overruns.Should().BeEmpty();
    }

    [Fact(DisplayName = "N5: a block left in progress on an earlier day never pings")]
    public async Task OldInProgressBlock_IsNotAnnounced()
    {
        await SeedBlockAsync(UserId, Tuesday.AddDays(-1), new TimeOnly(12, 0), new TimeOnly(13, 0), PlannerTaskStatus.InProgress);

        var notifier = new NotifierDouble();
        await RunJobAsync(At(10, 0), notifier);

        notifier.Overruns.Should().BeEmpty();
    }

    /// <summary>
    /// The <c>IRunningTimerLookup</c> seam, both directions: a pending alarm holds the nudge (and leaves it unmarked, so
    /// it comes once the timer stops), and a past one — a stale row the boot registrar has not swept — holds nothing.
    /// </summary>
    [Fact(DisplayName = "N5: the overrun waits while the user has a timer running, and comes once it stops")]
    public async Task RunningTimer_HoldsTheOverrun()
    {
        var blockId = await SeedBlockAsync(UserId, Tuesday, new TimeOnly(12, 0), new TimeOnly(13, 0), PlannerTaskStatus.InProgress);
        var alarmId = await SeedTimerAlarmAsync(UserId, At(13, 25).UtcDateTime);

        var notifier = new NotifierDouble();
        await RunJobAsync(At(13, 10), notifier);

        notifier.Overruns.Should().BeEmpty("a timer is running");
        (await ReadMarksAsync(blockId)).Overrun.Should().BeNull();

        // The alarm's instant has passed and its row was never cleaned up: that is not a running timer.
        await RunJobAsync(At(13, 30), notifier);
        notifier.Overruns.Should().ContainSingle().Which.MinutesOver.Should().Be(30);

        await using var db = CreateDbContext();
        (await db.Set<TimerBoundaryAlarm>().IgnoreQueryFilters().AnyAsync(a => a.Id == alarmId, CancellationToken)).Should().BeTrue();
    }

    // ---- the real pipeline ------------------------------------------------------------------------

    [Fact(DisplayName = "N5: the real pipeline stores PlannedBlocksMissed with the date, count and the earliest block as subject")]
    public async Task RealPipeline_Missed()
    {
        var earliest = await SeedBlockAsync(UserId, Tuesday, new TimeOnly(8, 0), new TimeOnly(9, 0));
        await SeedBlockAsync(UserId, Tuesday, new TimeOnly(9, 0), new TimeOnly(10, 0));

        await RunJobAsync(At(10, 5));

        var notification = await SingleNotificationAsync(PlanningNotificationTypes.PlannedBlocksMissed);
        var payload = JsonDocument.Parse(notification.PayloadJson).RootElement;
        payload.GetProperty("date").GetString().Should().Be("2020-09-29");
        payload.GetProperty("count").GetInt32().Should().Be(2);

        var renderer = Fixture.AdminAndUserFactory.Services.GetRequiredService<INotificationTextRenderer>();
        renderer.RenderSubject(notification.Type, notification.PayloadJson)
            .Should().Be(new NotificationSubject(NotificationSubjectKinds.PlannerTask, earliest));
        renderer.Render(notification.Type, notification.PayloadJson, AvailableLocales.En).Body
            .Should().Be("2 planned blocks slipped by — re-plan the rest of today?");
    }

    [Fact(DisplayName = "N5: the real pipeline stores PlannedBlockOverrun, worded with the activity's name")]
    public async Task RealPipeline_Overrun()
    {
        var activity = $"Deep work {Guid.NewGuid():N}";
        var blockId = await SeedBlockAsync(UserId, Tuesday, new TimeOnly(12, 0), new TimeOnly(13, 0), PlannerTaskStatus.InProgress, activityName: activity);

        await RunJobAsync(At(13, 10));

        var notification = await SingleNotificationAsync(PlanningNotificationTypes.PlannedBlockOverrun);
        var renderer = Fixture.AdminAndUserFactory.Services.GetRequiredService<INotificationTextRenderer>();
        renderer.RenderSubject(notification.Type, notification.PayloadJson)
            .Should().Be(new NotificationSubject(NotificationSubjectKinds.PlannerTask, blockId));
        renderer.Render(notification.Type, notification.PayloadJson, AvailableLocales.En).Body
            .Should().Be($"Time for “{activity}” ran out 10 min ago — wrap up or extend?");
    }

    /// <summary>The wording, in both tables — Slovak agrees noun, adjective and verb with the count.</summary>
    [Theory(DisplayName = "N5: the missed body agrees with the count in both languages")]
    [InlineData(1, AvailableLocales.En, "1 planned block slipped by — re-plan the rest of today?")]
    [InlineData(3, AvailableLocales.En, "3 planned blocks slipped by — re-plan the rest of today?")]
    [InlineData(1, AvailableLocales.Sk, "1 naplánovaný blok ušiel — preplánovať zvyšok dňa?")]
    [InlineData(3, AvailableLocales.Sk, "3 naplánované bloky ušli — preplánovať zvyšok dňa?")]
    [InlineData(5, AvailableLocales.Sk, "5 naplánovaných blokov ušlo — preplánovať zvyšok dňa?")]
    public void MissedBody_AgreesWithTheCount(int count, AvailableLocales locale, string expected)
    {
        var renderer = Fixture.AdminAndUserFactory.Services.GetRequiredService<INotificationTextRenderer>();
        var json = JsonSerializer.Serialize(new { date = "2020-09-29", count, firstTaskId = 1 });

        renderer.Render(PlanningNotificationTypes.PlannedBlocksMissed, json, locale).Body.Should().Be(expected);
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private async Task<long> SeedBlockAsync(
        long userId,
        DateOnly date,
        TimeOnly start,
        TimeOnly end,
        PlannerTaskStatus status = PlannerTaskStatus.NotStarted,
        bool autoComplete = false,
        bool background = false,
        DayType dayType = DayType.Workday,
        string? activityName = null,
        DateTime? createdAt = null)
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, activityName ?? $"block-{Guid.NewGuid():N}", userId, CancellationToken);
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, date, userId, CancellationToken, dayType);
        var taskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, start, end, userId, status, ct: CancellationToken);

        // ExecuteUpdate, so no interceptor restamps the creation time.
        await db.Set<PlannerTask>().IgnoreQueryFilters().Where(t => t.Id == taskId).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.AutoCompleteAtEnd, autoComplete)
            .SetProperty(t => t.IsBackground, background)
            .SetProperty(t => t.CreatedTimestamp, createdAt ?? new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)), CancellationToken);
        return taskId;
    }

    private async Task<long> SeedTimerAlarmAsync(long userId, DateTime boundaryAt)
    {
        await using var db = CreateDbContext();
        var alarm = new TimerBoundaryAlarm
        {
            UserId = userId,
            SessionId = Guid.NewGuid(),
            BoundaryAt = boundaryAt,
            Phase = TimerBoundaryPhase.Focus
        };
        db.Add(alarm);
        await db.SaveChangesAsync(CancellationToken);
        return alarm.Id;
    }

    private async Task<(DateTime? Missed, DateTime? Overrun)> ReadMarksAsync(long taskId)
    {
        await using var db = CreateDbContext();
        var task = await db.Set<PlannerTask>().IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == taskId, CancellationToken);
        return (task.MissedNotifiedAt, task.OverrunNotifiedAt);
    }

    private async Task<Notification> SingleNotificationAsync(NotificationType type)
    {
        await using var db = CreateDbContext();
        return await db.Set<Notification>().AsNoTracking().SingleAsync(n => n.UserId == UserId && n.Type == type, CancellationToken);
    }

    private static async Task EnsureUserAsync(DbContext db, long userId, TimeZoneInfo timezone, CancellationToken ct)
    {
        var email = $"slip-{userId}@test.com";
        var user = new User
        {
            Id = userId,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            Timezone = timezone
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, AppDbContextFixture.TestUserPassword);
        db.Set<User>().Add(user);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Runs the job the way the Scheduler dispatcher does — one scope per fire, no ambient user — with the clock pinned
    /// at <paramref name="now"/>, and the producer substituted when a double is given.
    /// </summary>
    private async Task RunJobAsync(DateTimeOffset now, NotifierDouble? notifier = null)
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configureServices: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));

            services.RemoveAll<ILoggedUserService>();
            services.AddScoped<ILoggedUserService>(_ => new NoAmbientUserLoggedUserService());

            // History's boot registrar judges alarms against the real clock, so to it every 2020 alarm a case seeds is
            // stale, and it deletes the row as this host starts — before the sweep can see the timer running.
            foreach (var registrar in services.Where(d => d.ServiceType == typeof(IHostedService)
                                                          && d.ImplementationType == typeof(TimerBoundaryAlarmRegistrar)).ToList())
                services.Remove(registrar);

            if (notifier is not null)
            {
                services.RemoveAll<IPlannerNotificationService>();
                services.AddSingleton<IPlannerNotificationService>(notifier);
            }
        });

        await using var scope = factory.Services.CreateAsyncScope();
        var handler = ActivatorUtilities.CreateInstance<PlannerSlipNudgeJobHandler>(scope.ServiceProvider);

        await handler.ExecuteAsync(new ScheduledJobContext
        {
            ScheduledFireTimeUtc = now.UtcDateTime,
            ActualFireTimeUtc = now.UtcDateTime,
            JobKey = handler.Key,
            CorrelationId = "planner-slip-nudge-test",
            TriggerSource = TriggerSource.Manual
        }, CancellationToken);
    }
}
