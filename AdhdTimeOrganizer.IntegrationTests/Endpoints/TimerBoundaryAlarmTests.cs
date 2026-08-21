using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.History.application.job;
using AdhdTimeOrganizer.History.application.service;
using AdhdTimeOrganizer.History.domain.model.entity.timerAlarm;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.domain.entity;
using Sydowwe.Notifications.infrastructure;
using Sydowwe.Notifications.infrastructure.email;
using Sydowwe.Notifications.infrastructure.webPush;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The end-of-timer alarm, which exists because a <c>setTimeout</c> cannot fire in a closed tab: the client
/// registers every phase boundary when the user presses Start, and the server rings them from its own
/// one-shot triggers.
/// <para>
/// Every mechanism here fails <b>silently</b> — a handler resolved by string key, a job registered against a
/// RAM store that a restart empties, and a push document whose keys the service worker reads positionally.
/// None of them produce a build error or an exception when they break; the alarm simply never arrives. So
/// these assert on rows and on the bytes that go over the wire, never on routes.
/// </para>
/// </summary>
[Collection("Postgres")]
public class TimerBoundaryAlarmTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long TestUserId = FakeLoggedUserService.TestUserId;

    private const string ScheduleRoute = "api/activity-history/timer-alarm/schedule";
    private const string CancelRoute = "api/activity-history/timer-alarm/cancel";

    /// <summary>
    /// The fixture sets every MAIL_* var, so SMTP auto-detects as configured and the dispatcher would open a
    /// socket to a fake host. TimerBoundary is email-default-OFF anyway (it is absent from
    /// <c>NotificationChannelDefaults.EmailOnByDefault</c>) — this pins the master switch so these tests can
    /// never become network calls.
    /// </summary>
    private static readonly KeyValuePair<string, string?>[] EmailOff =
        [new($"{EmailNotificationOptions.SectionName}:Enabled", "false")];

    /// <summary>VAPID keys so <c>PushNotificationOptions.IsConfigured</c> is true and the Web Push branch runs at all.</summary>
    private static readonly KeyValuePair<string, string?>[] PushOn =
    [
        new($"{EmailNotificationOptions.SectionName}:Enabled", "false"),
        new($"{PushNotificationOptions.SectionName}:VapidSubject", "mailto:test@example.com"),
        new($"{PushNotificationOptions.SectionName}:VapidPublicKey", "test-public-key"),
        new($"{PushNotificationOptions.SectionName}:VapidPrivateKey", "test-private-key")
    ];

    // ---- registration -----------------------------------------------------------------------------

    [Fact(DisplayName = "Start registers one row per future boundary and silently drops elapsed ones")]
    public async Task Schedule_WritesOneRowPerFutureBoundary()
    {
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var response = await factory.CreateClient().PostAsJsonAsync(ScheduleRoute, new
        {
            SessionId = sessionId,
            Url = "/activity-history/pomodoro",
            Boundaries = new object[]
            {
                // Already elapsed: accepted by the validator, dropped by the service. A set re-sent after a
                // slow reload legitimately contains these, and failing the whole request would cost the user
                // the alarms that are still to come.
                new { BoundaryAt = now.AddMinutes(-5), Phase = TimerBoundaryPhase.Focus, PhaseIndex = 1, PhaseTotal = 4, CycleIndex = 1, CycleTotal = 2 },
                new { BoundaryAt = now.AddMinutes(25), Phase = TimerBoundaryPhase.Focus, PhaseIndex = 2, PhaseTotal = 4, CycleIndex = 1, CycleTotal = 2 },
                new { BoundaryAt = now.AddMinutes(30), Phase = TimerBoundaryPhase.ShortBreak, PhaseIndex = 2, PhaseTotal = 4, CycleIndex = 1, CycleTotal = 2 }
            }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = CreateDbContext();
        var alarms = await db.Set<TimerBoundaryAlarm>().OrderBy(a => a.BoundaryAt).ToListAsync(CancellationToken);

        alarms.Should().HaveCount(2, "the boundary already in the past is dropped rather than fired immediately");
        alarms.Should().OnlyContain(a => a.UserId == TestUserId && a.SessionId == sessionId);
        alarms.Should().OnlyContain(a => a.Url == "/activity-history/pomodoro");
        alarms[1].Phase.Should().Be(TimerBoundaryPhase.ShortBreak);
    }

    [Fact(DisplayName = "Re-posting a session replaces its alarms instead of duplicating them")]
    public async Task Schedule_IsIdempotentBySession()
    {
        var sessionId = Guid.NewGuid();

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var client = factory.CreateClient();

        await PostSessionAsync(client, sessionId, DateTime.UtcNow.AddMinutes(25));
        await PostSessionAsync(client, sessionId, DateTime.UtcNow.AddMinutes(40));

        await using var db = CreateDbContext();
        var alarms = await db.Set<TimerBoundaryAlarm>().ToListAsync(CancellationToken);

        alarms.Should().ContainSingle("re-registering a session replaces its set — a client re-sending after a "
                                     + "reload must not have to know what landed the first time");
        alarms[0].BoundaryAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(40), TimeSpan.FromMinutes(1));
    }

    // ---- cancellation -----------------------------------------------------------------------------

    [Fact(DisplayName = "Stop cancels every alarm of the session in one call")]
    public async Task Cancel_RemovesTheWholeSession()
    {
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var client = factory.CreateClient();
        await PostSessionAsync(client, sessionId, now.AddMinutes(25), now.AddMinutes(30));

        var response = await client.PostAsJsonAsync(CancelRoute, new { SessionId = sessionId }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = CreateDbContext();
        (await db.Set<TimerBoundaryAlarm>().CountAsync(CancellationToken)).Should().Be(0);
    }

    [Fact(DisplayName = "Cancelling an unknown session is a no-op, not a 404")]
    public async Task Cancel_OnUnknownSession_IsANoOp()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);

        var response = await factory.CreateClient()
            .PostAsJsonAsync(CancelRoute, new { SessionId = Guid.NewGuid() }, JsonOpts, CancellationToken);

        // A partially-registered set is the normal outcome of a tab dying mid-flight, and the client cancels
        // the whole set regardless of what it believes landed. "Nothing to cancel" is success.
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ---- firing -----------------------------------------------------------------------------------

    [Fact(DisplayName = "The alarm fires into notification history and deletes its own row")]
    public async Task Handler_Fires_ThenSelfDrains()
    {
        var sessionId = Guid.NewGuid();

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        await PostSessionAsync(factory.CreateClient(), sessionId, DateTime.UtcNow.AddMinutes(25));

        long alarmId;
        await using (var db = CreateDbContext())
            alarmId = (await db.Set<TimerBoundaryAlarm>().SingleAsync(CancellationToken)).Id;

        await FireAsync(factory, alarmId);

        await using var after = CreateDbContext();

        var notification = await after.Set<Notification>().SingleAsync(CancellationToken);
        notification.UserId.Should().Be(TestUserId);
        notification.Type.Should().Be(NotificationType.TimerBoundary);
        notification.DeferredUntil.Should().BeNull();

        (await after.Set<TimerBoundaryAlarm>().CountAsync(CancellationToken))
            .Should().Be(0, "the table self-drains on fire — that is why it needs no retention job");
    }

    [Fact(DisplayName = "An alarm cancelled between the trigger and the fire produces silence, not a failure")]
    public async Task Handler_OnCancelledAlarm_DoesNothing()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);

        // Nothing was ever registered under this id — the same state a cancel that raced the fire leaves.
        await FireAsync(factory, alarmId: 987654);

        await using var db = CreateDbContext();
        (await db.Set<Notification>().CountAsync(CancellationToken)).Should().Be(0);
    }

    // ---- the wire format the service worker depends on --------------------------------------------

    [Fact(DisplayName = "The push document carries title, body, tag and url at the TOP level")]
    public async Task PushEnvelope_CarriesTagAndUrlAtTopLevel()
    {
        var sessionId = Guid.NewGuid();
        var boundaryAt = DateTime.UtcNow.AddMinutes(25);
        var sender = new CapturingWebPushSender();

        await using var factory = CreateFactory(
            TestRoles.AdminAndUser,
            configureServices: services => services.AddSingleton<IWebPushSender>(sender),
            configOverrides: PushOn);

        await SeedPushSubscriptionAsync();
        await PostSessionAsync(factory.CreateClient(), sessionId, boundaryAt);

        long alarmId;
        string expectedTag;
        await using (var db = CreateDbContext())
        {
            var alarm = await db.Set<TimerBoundaryAlarm>().SingleAsync(CancellationToken);
            alarmId = alarm.Id;
            expectedTag = alarm.Tag;
        }

        await FireAsync(factory, alarmId);

        var payload = sender.Payloads.Should().ContainSingle().Subject;
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        // sw-push.js does `var data = event.data.json()` and reads data.title / data.body / data.url /
        // data.tag. Nesting any of these does not fail — it renders a push titled with the app name and an
        // empty body, which looks like it worked. That is why this asserts on the parsed document.
        root.TryGetProperty("title", out var title).Should().BeTrue();
        title.GetString().Should().NotBeNullOrWhiteSpace();

        root.TryGetProperty("body", out var body).Should().BeTrue();
        body.GetString().Should().NotBeNullOrWhiteSpace();

        root.TryGetProperty("url", out var url).Should().BeTrue("the service worker reads url off the top level");
        url.GetString().Should().Be("/activity-history/pomodoro");

        root.TryGetProperty("tag", out var tag).Should().BeTrue("the tag is how the double alarm is avoided");
        tag.GetString().Should().Be(expectedTag);

        // Derived from the boundary instant, not the row id, so the still-open tab can rebuild the identical
        // tag with no round trip — which is the only reason replace-in-place works.
        tag.GetString().Should().Be(TimerBoundaryAlarm.TagFor(boundaryAt));

        // A silent replacement. Setting renotify would turn the suppression of the on-screen notification
        // into a second buzz — the opposite of what the tag is for.
        root.TryGetProperty("renotify", out _).Should().BeFalse();
    }

    [Fact(DisplayName = "An untagged notification kind gets no tag key at all, so it cannot replace others")]
    public async Task PushEnvelope_OmitsTagForKindsThatSupplyNone()
    {
        var sender = new CapturingWebPushSender();

        await using var factory = CreateFactory(
            TestRoles.AdminAndUser,
            configureServices: services => services.AddSingleton<IWebPushSender>(sender),
            configOverrides: PushOn);

        await SeedPushSubscriptionAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
            await notifications.NotifyAsync(
                NotificationRecipients.User(TestUserId), new TestNotificationPayload("hello"), CancellationToken);
        }

        var payload = sender.Payloads.Should().ContainSingle().Subject;
        using var document = JsonDocument.Parse(payload);

        // Omitted, not written as null: showNotification treats an explicit null tag as a tag, which would
        // make every untagged notification replace every other one.
        document.RootElement.TryGetProperty("tag", out _).Should().BeFalse();
        document.RootElement.TryGetProperty("url", out _).Should().BeFalse();
    }

    // ---- quiet hours ------------------------------------------------------------------------------

    [Fact(DisplayName = "A timer alarm is delivered inside quiet hours; an ordinary notification is deferred")]
    public async Task TimerBoundary_IsExemptFromQuietHoursDeferral()
    {
        var sessionId = Guid.NewGuid();

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);

        // A window covering effectively the whole day, so "now" is inside it whenever this test runs.
        await using (var seed = CreateDbContext())
        {
            seed.Add(new NotificationQuietHours { UserId = TestUserId, StartMinute = 0, EndMinute = 1439 });
            await seed.SaveChangesAsync(CancellationToken);
        }

        await PostSessionAsync(factory.CreateClient(), sessionId, DateTime.UtcNow.AddMinutes(25));

        long alarmId;
        await using (var db = CreateDbContext())
            alarmId = (await db.Set<TimerBoundaryAlarm>().SingleAsync(CancellationToken)).Id;

        await FireAsync(factory, alarmId);

        // The control: same window, same user, a kind that is not exempt.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
            await notifications.NotifyAsync(
                NotificationRecipients.User(TestUserId), new TestNotificationPayload("hello"), CancellationToken);
        }

        await using var after = CreateDbContext();
        var byType = await after.Set<Notification>().ToDictionaryAsync(n => n.Type, CancellationToken);

        // The user started this timer minutes ago and asked for this exact instant. Deferring it to the end
        // of the window delivers "focus period ended" hours late, which is worse than not delivering it.
        byType[NotificationType.TimerBoundary].DeferredUntil
            .Should().BeNull("TimerBoundary is quiet-hours-exempt — the user scheduled this instant themselves");

        byType[NotificationType.Test].DeferredUntil
            .Should().NotBeNull("an ordinary kind is still deferred — the exemption is per-kind, not a switch-off");
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private Task PostSessionAsync(HttpClient client, Guid sessionId, params DateTime[] boundaries) =>
        client.PostAsJsonAsync(ScheduleRoute, new
        {
            SessionId = sessionId,
            Url = "/activity-history/pomodoro",
            Boundaries = boundaries.Select(at => new
            {
                BoundaryAt = at,
                Phase = TimerBoundaryPhase.Focus,
                PhaseIndex = 1,
                PhaseTotal = 4,
                CycleIndex = 1,
                CycleTotal = 2
            }).ToArray()
        }, JsonOpts, CancellationToken);

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

    /// <summary>
    /// Drives the handler directly rather than waiting on Quartz — the trigger's only contribution is the
    /// fire instant, and the handler takes everything it needs from the payload.
    /// <para>
    /// Resolving it out of <c>IEnumerable&lt;IScheduledJobHandler&gt;</c> by <c>Key</c> is deliberate: that is
    /// exactly how the Scheduler's dispatcher finds it, so a renamed or double-registered handler key fails
    /// here instead of in production silence.
    /// </para>
    /// </summary>
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
            CorrelationId = "timer-alarm-test",
            TriggerSource = TriggerSource.Manual
        }, CancellationToken);
    }

    /// <summary>Captures the exact bytes that would go to the push service, so the wire format can be asserted.</summary>
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
