using AdhdTimeOrganizer.History.application.notification;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.History.application.job;
using AdhdTimeOrganizer.History.application.service;
using AdhdTimeOrganizer.History.domain.model.entity.timerAlarm;
using AdhdTimeOrganizer.History.domain.model.@enum;
using AdhdTimeOrganizer.History.infrastructure.scheduling;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.domain.entity;
using Sydowwe.Notifications.infrastructure;
using Sydowwe.Notifications.infrastructure.email;
using Sydowwe.Notifications.infrastructure.webPush;
using Sydowwe.Scheduler.domain.entity;
using Sydowwe.Scheduler.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// P3-03: a running stopwatch checks in — "still on X? 1 h 30 min". It rides the timer-boundary alarm table, triggers
/// and boot registrar, so every silent failure <see cref="TimerBoundaryAlarmTests"/> pins applies here too; what is
/// new is the kind on the row, the validator's per-kind shape, and that quiet hours <b>skip</b> a check-in instead of
/// deferring it. Asserted on rows and on the bytes that would go to the push service.
/// </summary>
[Collection("Postgres")]
public class StopwatchCheckInAlarmTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long TestUserId = FakeLoggedUserService.TestUserId;

    private const string ScheduleRoute = "api/activity-history/timer-alarm/schedule";
    private const string CancelRoute = "api/activity-history/timer-alarm/cancel";
    private const string StopwatchUrl = "/activity-history/stopwatch";

    private static readonly KeyValuePair<string, string?>[] EmailOff =
        [new($"{EmailNotificationOptions.SectionName}:Enabled", "false")];

    private static readonly KeyValuePair<string, string?>[] PushOn =
    [
        new($"{EmailNotificationOptions.SectionName}:Enabled", "false"),
        new($"{PushNotificationOptions.SectionName}:VapidSubject", "mailto:test@example.com"),
        new($"{PushNotificationOptions.SectionName}:VapidPublicKey", "test-public-key"),
        new($"{PushNotificationOptions.SectionName}:VapidPrivateKey", "test-private-key")
    ];

    // ---- registration and firing ------------------------------------------------------------------

    [Fact(DisplayName = "A check-in set is stored as check-in rows, and firing one sends a StopwatchCheckIn with the running time and the activity")]
    public async Task CheckIn_Fires_WithElapsedMinutesAndActivityName()
    {
        const string activityName = "Deep Reading";
        var sender = new CapturingWebPushSender();
        var checkInAt = DateTime.UtcNow.AddMinutes(30);

        await using var factory = CreateFactory(
            TestRoles.AdminAndUser,
            configureServices: services => services.AddSingleton<IWebPushSender>(sender),
            configOverrides: PushOn);

        await SeedPushSubscriptionAsync();
        long activityId;
        await using (var seed = CreateDbContext())
            activityId = await SeedActivityAsync(seed, activityName);

        var response = await PostCheckInsAsync(factory.CreateClient(), Guid.NewGuid(), activityId,
            (checkInAt, 90), (checkInAt.AddHours(1), 150));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        TimerBoundaryAlarm first;
        await using (var db = CreateDbContext())
        {
            var alarms = await db.Set<TimerBoundaryAlarm>().OrderBy(a => a.BoundaryAt).ToListAsync(CancellationToken);
            alarms.Should().HaveCount(2);
            alarms.Should().OnlyContain(a => a.Kind == TimerAlarmKind.StopwatchCheckIn && a.Phase == null);
            alarms.Select(a => a.ElapsedMinutes).Should().Equal(90, 150);
            first = alarms[0];
        }

        await FireAsync(factory, first.Id);

        await using var after = CreateDbContext();
        var notification = await after.Set<Notification>().SingleAsync(CancellationToken);
        notification.UserId.Should().Be(TestUserId);
        notification.Type.Should().Be(HistoryNotificationTypes.StopwatchCheckIn);
        notification.DeferredUntil.Should().BeNull();

        var payload = JsonSerializer.Deserialize<StopwatchCheckInPayload>(notification.PayloadJson, JsonOpts)!;
        payload.ElapsedMinutes.Should().Be(90);
        payload.ActivityName.Should().Be(activityName);

        (await after.Set<TimerBoundaryAlarm>().Select(a => a.Id).ToListAsync(CancellationToken))
            .Should().NotContain(first.Id, "the row self-drains on fire")
            .And.HaveCount(1, "the later check-in is still pending");

        // The push the service worker receives: worded from the payload, tagged and routed at the top level.
        using var document = JsonDocument.Parse(sender.Payloads.Should().ContainSingle().Subject);
        var root = document.RootElement;
        root.GetProperty("title").GetString().Should().Contain(activityName);
        root.GetProperty("body").GetString().Should().Contain("1 h 30 min");
        root.GetProperty("url").GetString().Should().Be(StopwatchUrl);
        root.GetProperty("tag").GetString().Should().Be(TimerBoundaryAlarm.CheckInTagFor(first.BoundaryAt))
            .And.NotBe(TimerBoundaryAlarm.TagFor(first.BoundaryAt), "a check-in must never collapse into a phase boundary");
    }

    [Fact(DisplayName = "A boundary alarm still fires as TimerBoundary")]
    public async Task Boundary_StillFiresAsTimerBoundary()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);

        var response = await factory.CreateClient().PostAsJsonAsync(ScheduleRoute, new
        {
            SessionId = Guid.NewGuid(),
            Boundaries = new object[] { new { BoundaryAt = DateTime.UtcNow.AddMinutes(25), Phase = TimerBoundaryPhase.Focus } }
        }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        TimerBoundaryAlarm alarm;
        await using (var db = CreateDbContext())
            alarm = await db.Set<TimerBoundaryAlarm>().SingleAsync(CancellationToken);
        alarm.Kind.Should().Be(TimerAlarmKind.Boundary, "a request without a kind is a phase boundary, as before check-ins existed");

        await FireAsync(factory, alarm.Id);

        await using var after = CreateDbContext();
        (await after.Set<Notification>().SingleAsync(CancellationToken)).Type.Should().Be(HistoryNotificationTypes.TimerBoundary);
    }

    // ---- quiet hours ------------------------------------------------------------------------------

    /// <summary>
    /// The dispatcher can only defer, so a check-in inside quiet hours would arrive at the end of the window about a
    /// stopwatch from the middle of the night — and its bell row would say the same. The handler skips it instead:
    /// no notification row at all, and the alarm row gone like any fired one.
    /// </summary>
    [Fact(DisplayName = "A check-in inside quiet hours sends nothing, writes no bell row, and deletes its alarm")]
    public async Task CheckIn_InsideQuietHours_IsSkipped()
    {
        // Push configured, so without the skip the dispatcher would write a row and defer it — the assertion below
        // cannot pass merely because no channel could deliver.
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: PushOn);

        await using (var seed = CreateDbContext())
        {
            // A window covering effectively the whole day, so "now" is inside it whenever this test runs.
            seed.Add(new NotificationQuietHours { UserId = TestUserId, StartMinute = 0, EndMinute = 1439 });
            await seed.SaveChangesAsync(CancellationToken);
        }

        await PostCheckInsAsync(factory.CreateClient(), Guid.NewGuid(), null, (DateTime.UtcNow.AddMinutes(30), 90));

        long alarmId;
        await using (var db = CreateDbContext())
            alarmId = (await db.Set<TimerBoundaryAlarm>().SingleAsync(CancellationToken)).Id;

        await FireAsync(factory, alarmId);

        await using var after = CreateDbContext();
        (await after.Set<Notification>().CountAsync(CancellationToken))
            .Should().Be(0, "a check-in is skipped inside quiet hours, not deferred — not even a bell row");
        (await after.Set<TimerBoundaryAlarm>().CountAsync(CancellationToken))
            .Should().Be(0, "the skipped alarm self-drains like a fired one");
    }

    // ---- cancellation and boot --------------------------------------------------------------------

    [Fact(DisplayName = "Cancel removes the whole check-in set")]
    public async Task Cancel_RemovesTheCheckInSet()
    {
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var client = factory.CreateClient();
        await PostCheckInsAsync(client, sessionId, null, (now.AddMinutes(30), 90), (now.AddMinutes(90), 150));

        var response = await client.PostAsJsonAsync(CancelRoute, new { SessionId = sessionId }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = CreateDbContext();
        (await db.Set<TimerBoundaryAlarm>().CountAsync(CancellationToken)).Should().Be(0);
    }

    /// <summary>
    /// The job store is in memory, so a restart loses every trigger and only the registrar brings them back. A
    /// removed registry row stands in for the lost trigger: the registrar must make it active again, for a check-in
    /// exactly as for a boundary.
    /// </summary>
    [Fact(DisplayName = "The boot registrar re-registers a pending check-in")]
    public async Task Registrar_ReRegistersCheckIns()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        await PostCheckInsAsync(factory.CreateClient(), Guid.NewGuid(), null, (DateTime.UtcNow.AddMinutes(30), 90));

        string jobKey;
        await using (var db = CreateDbContext())
            jobKey = (await db.Set<TimerBoundaryAlarm>().SingleAsync(CancellationToken)).JobKey;

        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IScheduler>().RemoveJobAsync(jobKey, CancellationToken);
        (await StatusOfAsync(jobKey)).Should().Be(JobStatus.Removed);

        await new TimerBoundaryAlarmRegistrar(factory.Services, NullLogger<TimerBoundaryAlarmRegistrar>.Instance)
            .StartAsync(CancellationToken);

        (await StatusOfAsync(jobKey)).Should().Be(JobStatus.Active, "the check-in row is still pending, so boot re-registers it");
    }

    // ---- the overrun nudge's running-timer seam ---------------------------------------------------

    /// <summary>
    /// Planning holds its "time's up" nudge while a timer is running, because a countdown or a pomodoro rings at its own
    /// end. A stopwatch has no end, so its check-in rows must not count — otherwise every stopwatch user silently
    /// loses the nudge.
    /// </summary>
    [Fact(DisplayName = "A pending check-in does not make a stopwatch count as a running timer; a boundary does")]
    public async Task RunningTimerLookup_IgnoresCheckIns()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        await PostCheckInsAsync(factory.CreateClient(), Guid.NewGuid(), null, (DateTime.UtcNow.AddMinutes(30), 90));

        await using var scope = factory.Services.CreateAsyncScope();
        var lookup = scope.ServiceProvider.GetRequiredService<IRunningTimerLookup>();
        var db = scope.ServiceProvider.GetRequiredService<DbContext>();

        (await lookup.GetUsersWithRunningTimerAsync(db, [TestUserId], DateTime.UtcNow, CancellationToken))
            .Should().BeEmpty("a stopwatch's check-in is not a planned end");

        var boundary = await factory.CreateClient().PostAsJsonAsync(ScheduleRoute, new
        {
            SessionId = Guid.NewGuid(),
            Boundaries = new object[] { new { BoundaryAt = DateTime.UtcNow.AddMinutes(25), Phase = TimerBoundaryPhase.Focus } }
        }, JsonOpts, CancellationToken);
        boundary.StatusCode.Should().Be(HttpStatusCode.OK);

        (await lookup.GetUsersWithRunningTimerAsync(db, [TestUserId], DateTime.UtcNow, CancellationToken))
            .Should().Equal(TestUserId);
    }

    // ---- validator --------------------------------------------------------------------------------

    [Theory(DisplayName = "A check-in carrying a phase or cycle field is rejected")]
    [InlineData("phase")]
    [InlineData("phaseIndex")]
    [InlineData("phaseTotal")]
    [InlineData("cycleIndex")]
    [InlineData("cycleTotal")]
    public async Task CheckIn_WithPhaseField_IsRejected(string field)
    {
        var checkIn = new Dictionary<string, object?>
        {
            ["boundaryAt"] = DateTime.UtcNow.AddMinutes(30),
            ["elapsedMinutes"] = 90,
            [field] = field == "phase" ? TimerBoundaryPhase.Plain : 1
        };

        (await PostRawAsync(TimerAlarmKind.StopwatchCheckIn, checkIn)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory(DisplayName = "A check-in without a positive elapsedMinutes is rejected")]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task CheckIn_WithoutPositiveElapsed_IsRejected(int? elapsedMinutes)
    {
        var checkIn = new Dictionary<string, object?>
        {
            ["boundaryAt"] = DateTime.UtcNow.AddMinutes(30),
            ["elapsedMinutes"] = elapsedMinutes
        };

        (await PostRawAsync(TimerAlarmKind.StopwatchCheckIn, checkIn)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "A boundary carrying elapsedMinutes is rejected")]
    public async Task Boundary_WithElapsed_IsRejected()
    {
        var boundary = new Dictionary<string, object?>
        {
            ["boundaryAt"] = DateTime.UtcNow.AddMinutes(25),
            ["phase"] = TimerBoundaryPhase.Focus,
            ["elapsedMinutes"] = 25
        };

        (await PostRawAsync(TimerAlarmKind.Boundary, boundary)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "A boundary without a phase is rejected")]
    public async Task Boundary_WithoutPhase_IsRejected()
    {
        var boundary = new Dictionary<string, object?> { ["boundaryAt"] = DateTime.UtcNow.AddMinutes(25) };

        (await PostRawAsync(TimerAlarmKind.Boundary, boundary)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private Task<HttpResponseMessage> PostCheckInsAsync(
        HttpClient client, Guid sessionId, long? activityId, params (DateTime At, int ElapsedMinutes)[] checkIns) =>
        client.PostAsJsonAsync(ScheduleRoute, new
        {
            SessionId = sessionId,
            Kind = TimerAlarmKind.StopwatchCheckIn,
            ActivityId = activityId,
            Url = StopwatchUrl,
            Boundaries = checkIns.Select(c => new { BoundaryAt = c.At, c.ElapsedMinutes }).ToArray()
        }, JsonOpts, CancellationToken);

    private async Task<HttpResponseMessage> PostRawAsync(TimerAlarmKind kind, Dictionary<string, object?> boundary)
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        return await factory.CreateClient().PostAsJsonAsync(ScheduleRoute, new
        {
            SessionId = Guid.NewGuid(),
            Kind = kind,
            Boundaries = new[] { boundary }
        }, JsonOpts, CancellationToken);
    }

    private async Task<long> SeedActivityAsync(DbContext db, string name)
    {
        var role = new ActivityRole { UserId = TestUserId, Name = $"{name} Role {Guid.NewGuid():N}", Color = "#112233" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity { UserId = TestUserId, Name = name, RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);
        return activity.Id;
    }

    private async Task SeedPushSubscriptionAsync()
    {
        await using var db = CreateDbContext();
        db.Add(new PushSubscription
        {
            UserId = TestUserId,
            Endpoint = "https://push.example.test/endpoint",
            P256dh = "test-p256dh",
            Auth = "test-auth"
        });
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task<JobStatus> StatusOfAsync(string jobKey)
    {
        await using var db = CreateDbContext();
        return await db.Set<ScheduledJob>().Where(j => j.JobKey == jobKey).Select(j => j.Status).SingleAsync(CancellationToken);
    }

    /// <summary>Drives the handler the way the Scheduler's dispatcher finds it — by key — rather than waiting on Quartz.</summary>
    private async Task FireAsync(ITestClientFactory factory, long alarmId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IScheduledJobHandler>()
            .Single(h => h.Key == TimerBoundaryAlarmJobHandler.HandlerKey);

        var fireTime = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
        await handler.ExecuteAsync(new ScheduledJobContext
        {
            ScheduledFireTimeUtc = fireTime,
            ActualFireTimeUtc = fireTime,
            JobKey = TimerBoundaryAlarm.JobKeyFor(alarmId),
            PayloadJson = JsonSerializer.Serialize(
                new TimerBoundaryAlarmJobPayload(alarmId),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
            CorrelationId = "stopwatch-check-in-test",
            TriggerSource = TriggerSource.Manual
        }, CancellationToken);
    }

    private sealed class CapturingWebPushSender : IWebPushSender
    {
        public ConcurrentBag<string> Payloads { get; } = [];

        public Task<bool> SendAsync(PushTarget target, string payloadJson, CancellationToken ct = default)
        {
            Payloads.Add(payloadJson);
            return Task.FromResult(false);
        }
    }
}
