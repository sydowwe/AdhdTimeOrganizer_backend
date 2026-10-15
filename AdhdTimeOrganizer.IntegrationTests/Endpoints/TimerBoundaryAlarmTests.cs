using AdhdTimeOrganizer.History.domain.model.@enum;
using AdhdTimeOrganizer.History.application.notification;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.History.application.endpoint.command;
using AdhdTimeOrganizer.History.application.job;
using AdhdTimeOrganizer.History.application.service;
using AdhdTimeOrganizer.History.application.validator;
using AdhdTimeOrganizer.History.domain.model.entity.timerAlarm;
using AdhdTimeOrganizer.History.domain.serviceContract;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.domain.extServiceContract.user;
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

    /// <summary>
    /// Session guids are client-issued, so <c>CancelSessionAsync</c>'s <c>UserId</c> predicate is the only
    /// thing standing between a guessed guid and someone else's alarms. A no-op is still the right answer —
    /// cancel never leaks whether the guid belonged to anyone — but the other user's rows must survive it.
    /// </summary>
    [Fact(DisplayName = "Cancelling with another user's sessionId is a no-op and leaves their alarms intact")]
    public async Task Cancel_WithAnotherUsersSessionId_LeavesTheirAlarmsIntact()
    {
        var sessionId = Guid.NewGuid();

        await using var ownerFactory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        await PostSessionAsync(ownerFactory.CreateClient(), sessionId, DateTime.UtcNow.AddMinutes(25));

        var otherUserId = await ActivityMergeTestSupport.SecondUserAsync(Fixture, $"timer-alarm-cancel-{Guid.NewGuid():N}@test.com");
        await using var attackerFactory = CreateFactory(TestRoles.AdminAndUser, userId: otherUserId, configOverrides: EmailOff);

        var response = await attackerFactory.CreateClient()
            .PostAsJsonAsync(CancelRoute, new { SessionId = sessionId }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = CreateDbContext();
        (await db.Set<TimerBoundaryAlarm>().CountAsync(a => a.SessionId == sessionId, CancellationToken))
            .Should().Be(1, "cancel is scoped to the caller — a guessed session guid must not reach another user's rows");
    }

    /// <summary>
    /// The loser of a delete race: two overlapping cancels for one session (pause then Stop, or a replace
    /// racing a cancel), or a cancel racing the fire, each load the rows before the other deletes them. A
    /// tracked delete demands one affected row per entity and threw <c>DbUpdateConcurrencyException</c> — a
    /// 500 from an endpoint documented as idempotent. Deterministic stand-in: hand the service alarms whose
    /// rows are already gone.
    /// </summary>
    [Fact(DisplayName = "Cancelling alarms whose rows were already deleted is a no-op, not a concurrency failure")]
    public async Task Cancel_OfAlreadyDeletedRows_DoesNotThrow()
    {
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        await PostSessionAsync(factory.CreateClient(), sessionId, now.AddMinutes(25), now.AddMinutes(30));

        List<TimerBoundaryAlarm> loaded;
        await using (var db = CreateDbContext())
        {
            loaded = await db.Set<TimerBoundaryAlarm>().AsNoTracking().ToListAsync(CancellationToken);
            await db.Set<TimerBoundaryAlarm>().ExecuteDeleteAsync(CancellationToken);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ITimerBoundaryAlarmService>();

        var cancel = () => service.CancelAsync(loaded, CancellationToken);
        await cancel.Should().NotThrowAsync("deleting an already-deleted alarm is exactly what the caller wanted");
    }

    // ---- validator ----------------------------------------------------------------------------------

    [Fact(DisplayName = "Exactly MaxBoundaries boundaries is accepted")]
    public async Task Schedule_WithExactlyMaxBoundaries_IsAccepted()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var response = await factory.CreateClient().PostAsJsonAsync(ScheduleRoute, new
        {
            SessionId = Guid.NewGuid(),
            Boundaries = BuildBoundaries(ScheduleTimerAlarmsValidator.MaxBoundaries)
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "One more than MaxBoundaries is rejected")]
    public async Task Schedule_WithOneMoreThanMaxBoundaries_IsRejected()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var response = await factory.CreateClient().PostAsJsonAsync(ScheduleRoute, new
        {
            SessionId = Guid.NewGuid(),
            Boundaries = BuildBoundaries(ScheduleTimerAlarmsValidator.MaxBoundaries + 1)
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "An empty boundary list is rejected")]
    public async Task Schedule_WithEmptyBoundaryList_IsRejected()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var response = await factory.CreateClient().PostAsJsonAsync(ScheduleRoute, new
        {
            SessionId = Guid.NewGuid(),
            Boundaries = Array.Empty<object>()
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory(DisplayName = "A boundary right at MaxHorizon is accepted; past it is rejected")]
    [InlineData(23, HttpStatusCode.OK)]
    [InlineData(25, HttpStatusCode.BadRequest)]
    public async Task Schedule_AtHorizonBoundary_RespondsAccordingly(int hoursAhead, HttpStatusCode expected)
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var response = await factory.CreateClient().PostAsJsonAsync(ScheduleRoute, new
        {
            SessionId = Guid.NewGuid(),
            Boundaries = new object[] { new { BoundaryAt = DateTime.UtcNow.AddHours(hoursAhead), Phase = TimerBoundaryPhase.Focus } }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(expected);
    }

    [Fact(DisplayName = "An empty sessionId is rejected")]
    public async Task Schedule_WithEmptySessionId_IsRejected()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var response = await factory.CreateClient().PostAsJsonAsync(ScheduleRoute, new
        {
            SessionId = Guid.Empty,
            Boundaries = new object[] { new { BoundaryAt = DateTime.UtcNow.AddMinutes(25), Phase = TimerBoundaryPhase.Focus } }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory(DisplayName = "A non-positive activityId is rejected")]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Schedule_WithNonPositiveActivityId_IsRejected(long activityId)
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var response = await factory.CreateClient().PostAsJsonAsync(ScheduleRoute, new
        {
            SessionId = Guid.NewGuid(),
            ActivityId = activityId,
            Boundaries = new object[] { new { BoundaryAt = DateTime.UtcNow.AddMinutes(25), Phase = TimerBoundaryPhase.Focus } }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory(DisplayName = "A zero phase/cycle field is rejected")]
    [InlineData("phaseIndex")]
    [InlineData("phaseTotal")]
    [InlineData("cycleIndex")]
    [InlineData("cycleTotal")]
    public async Task Schedule_WithZeroPhaseOrCycleField_IsRejected(string field)
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);

        var boundary = new Dictionary<string, object?>
        {
            ["boundaryAt"] = DateTime.UtcNow.AddMinutes(25),
            ["phase"] = TimerBoundaryPhase.Focus,
            ["phaseIndex"] = 1,
            ["phaseTotal"] = 4,
            ["cycleIndex"] = 1,
            ["cycleTotal"] = 2
        };
        boundary[field] = 0;

        var response = await factory.CreateClient().PostAsJsonAsync(ScheduleRoute, new
        {
            SessionId = Guid.NewGuid(),
            Boundaries = new[] { boundary }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "A plain countdown supplying no phase/cycle fields is accepted")]
    public async Task Schedule_WithNullPhaseAndCycleFields_IsAccepted()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var response = await factory.CreateClient().PostAsJsonAsync(ScheduleRoute, new
        {
            SessionId = Guid.NewGuid(),
            Boundaries = new object[] { new { BoundaryAt = DateTime.UtcNow.AddMinutes(25), Phase = TimerBoundaryPhase.Focus } }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory(DisplayName = "Url must be a relative app route beginning with a single slash")]
    [InlineData("/activity-history/pomodoro", HttpStatusCode.OK)]
    [InlineData(null, HttpStatusCode.OK)]
    [InlineData("https://evil.example/x", HttpStatusCode.BadRequest)]
    [InlineData("//evil.example/x", HttpStatusCode.BadRequest)]
    [InlineData("javascript:alert(1)", HttpStatusCode.BadRequest)]
    [InlineData("activity-history/pomodoro", HttpStatusCode.BadRequest)]
    public async Task Schedule_UrlValidation_AcceptsOnlyARelativeRoute(string? url, HttpStatusCode expected)
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var response = await factory.CreateClient().PostAsJsonAsync(ScheduleRoute, new
        {
            SessionId = Guid.NewGuid(),
            Url = url,
            Boundaries = new object[] { new { BoundaryAt = DateTime.UtcNow.AddMinutes(25), Phase = TimerBoundaryPhase.Focus } }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(expected);
    }

    [Fact(DisplayName = "A url over 512 characters is rejected")]
    public async Task Schedule_WithUrlOverMaxLength_IsRejected()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var url = "/" + new string('a', 512); // 513 characters
        var response = await factory.CreateClient().PostAsJsonAsync(ScheduleRoute, new
        {
            SessionId = Guid.NewGuid(),
            Url = url,
            Boundaries = new object[] { new { BoundaryAt = DateTime.UtcNow.AddMinutes(25), Phase = TimerBoundaryPhase.Focus } }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// <c>ToUniversalTime</c> reads an <c>Unspecified</c>-kind instant as server-local time — a different
    /// interpretation from the one <see cref="TimerBoundaryAlarmService"/> stores it under (it force-labels the
    /// value UTC with <c>SpecifyKind</c>). Requiring the offset is what keeps validation and storage reading
    /// the same instant.
    /// </summary>
    [Fact(DisplayName = "A boundaryAt with no offset is rejected, not silently reinterpreted")]
    public async Task Schedule_WithUnspecifiedKindBoundaryAt_IsRejected()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var response = await factory.CreateClient().PostAsJsonAsync(ScheduleRoute, new
        {
            SessionId = Guid.NewGuid(),
            Boundaries = new object[]
            {
                new { BoundaryAt = "2026-03-10T12:25:00", Phase = TimerBoundaryPhase.Focus }
            }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "an instant with no offset is ambiguous between server-local and UTC — the contract requires one");
    }

    [Fact(DisplayName = "registered is the honest count of boundaries actually persisted")]
    public async Task Schedule_RegisteredCount_MatchesRowsWritten()
    {
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var response = await factory.CreateClient().PostAsJsonAsync(ScheduleRoute, new
        {
            SessionId = sessionId,
            Boundaries = new object[]
            {
                new { BoundaryAt = now.AddMinutes(-10), Phase = TimerBoundaryPhase.Focus },
                new { BoundaryAt = now.AddMinutes(-5), Phase = TimerBoundaryPhase.Focus },
                new { BoundaryAt = now.AddMinutes(10), Phase = TimerBoundaryPhase.Focus },
                new { BoundaryAt = now.AddMinutes(20), Phase = TimerBoundaryPhase.Focus },
                new { BoundaryAt = now.AddMinutes(30), Phase = TimerBoundaryPhase.Focus }
            }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ScheduleTimerAlarmsResponse>(JsonOpts, CancellationToken);
        body!.Registered.Should().Be(3, "two of the five boundaries are already in the past and are dropped");

        await using var db = CreateDbContext();
        (await db.Set<TimerBoundaryAlarm>().CountAsync(a => a.SessionId == sessionId, CancellationToken))
            .Should().Be(3, "the response field must not drift from the rows actually written");
    }

    private static object[] BuildBoundaries(int count)
    {
        var now = DateTime.UtcNow;
        return Enumerable.Range(0, count)
            .Select(i => (object)new { BoundaryAt = now.AddMinutes(5 + i), Phase = TimerBoundaryPhase.Focus })
            .ToArray();
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
        notification.Type.Should().Be(HistoryNotificationTypes.TimerBoundary);
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

    // ---- the client-supplied activity id ----------------------------------------------------------

    /// <summary>
    /// <c>ActivityId</c> is the one id on this request that is not the caller's own, and nothing downstream
    /// can check it: the row carries no FK (it lives minutes), and the handler that resolves the name into
    /// the push body runs unauthenticated, where the global user filter passes every row. So the endpoint is
    /// the only place ownership can be established, and it silently was not — a sequential guess returned
    /// another user's activity name in a push.
    /// </summary>
    [Fact(DisplayName = "Scheduling against another user's activityId is rejected and writes no rows")]
    public async Task Schedule_WithForeignActivityId_IsRejected()
    {
        var foreignActivityId = await SeedForeignActivityAsync("Foreign Timer Activity");

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var response = await PostSessionAsync(
            factory.CreateClient(), Guid.NewGuid(), foreignActivityId, DateTime.UtcNow.AddMinutes(25));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "an activity the caller does not own is indistinguishable from one that does not exist");

        await using var db = CreateDbContext();
        (await db.Set<TimerBoundaryAlarm>().CountAsync(CancellationToken))
            .Should().Be(0, "the whole set is refused — a rejected activity id must not leave alarms behind");
    }

    /// <summary>
    /// The control for the rejection above: the caller's own id is accepted and its name reaches the push
    /// body, so the check cannot be passing merely because the name is never rendered.
    /// </summary>
    [Fact(DisplayName = "The caller's own activityId is accepted and its name reaches the push body")]
    public async Task Schedule_WithOwnActivityId_RendersTheName()
    {
        const string ownName = "Own Timer Activity";
        var sender = new CapturingWebPushSender();

        await using var factory = CreateFactory(
            TestRoles.AdminAndUser,
            configureServices: services => services.AddSingleton<IWebPushSender>(sender),
            configOverrides: PushOn);

        await SeedPushSubscriptionAsync();

        long ownActivityId;
        await using (var seed = CreateDbContext())
            ownActivityId = await SeedActivityAsync(seed, TestUserId, ownName);

        var response = await PostSessionAsync(
            factory.CreateClient(), Guid.NewGuid(), ownActivityId, DateTime.UtcNow.AddMinutes(25));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        long alarmId;
        await using (var db = CreateDbContext())
        {
            var alarm = await db.Set<TimerBoundaryAlarm>().SingleAsync(CancellationToken);
            alarm.ActivityId.Should().Be(ownActivityId);
            alarmId = alarm.Id;
        }

        await FireAsync(factory, alarmId);

        var payload = sender.Payloads.Should().ContainSingle().Subject;
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("body").GetString()
            .Should().Contain(ownName, "resolving the name at fire time is the whole point of carrying the id");
    }

    /// <summary>
    /// Belt-and-braces for the same leak, one layer down: even a row that already carries a foreign activity
    /// id — written before the endpoint check existed, or by any future writer — must degrade to the
    /// name-less body rather than reading a stranger's row.
    /// <para>
    /// <b>The host here is deliberately unauthenticated</b>, which is what the Scheduler's dispatcher runs in.
    /// The default test host always has a logged user, so its <c>ScopeUserId</c> hides the foreign activity
    /// and the global filter would silently do this test's work — it would pass with the handler's own
    /// predicate deleted. Swapping <see cref="BackgroundLoggedUserService"/> in makes <c>ScopeUserId</c> null,
    /// the filter passes every row exactly as it does in production, and the predicate is the only thing left
    /// standing between the two users.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "The handler will not resolve an activity name off another user's row")]
    public async Task Handler_WithForeignActivityIdOnTheRow_RendersNoName()
    {
        const string foreignName = "Stranger Timer Activity";
        var sender = new CapturingWebPushSender();
        var boundaryAt = DateTime.UtcNow.AddMinutes(25);

        await using var factory = CreateFactory(
            TestRoles.AdminAndUser,
            configureServices: services =>
            {
                services.AddSingleton<IWebPushSender>(sender);
                services.AddScoped<ILoggedUserService, BackgroundLoggedUserService>();
            },
            configOverrides: PushOn);

        await SeedPushSubscriptionAsync();

        var foreignActivityId = await SeedForeignActivityAsync(foreignName);

        // Written straight to the table, bypassing the endpoint: the point is that the handler holds on its
        // own even when a bad row exists, so the endpoint's check must not be what makes this pass.
        long alarmId;
        await using (var seed = CreateDbContext())
        {
            var alarm = new TimerBoundaryAlarm
            {
                UserId = TestUserId,
                SessionId = Guid.NewGuid(),
                BoundaryAt = DateTime.SpecifyKind(boundaryAt, DateTimeKind.Utc),
                Phase = TimerBoundaryPhase.Focus,
                ActivityId = foreignActivityId,
                Url = "/activity-history/pomodoro"
            };
            seed.Add(alarm);
            await seed.SaveChangesAsync(CancellationToken);
            alarmId = alarm.Id;
        }

        await FireAsync(factory, alarmId);

        var payload = sender.Payloads.Should().ContainSingle().Subject;
        using var document = JsonDocument.Parse(payload);

        // The push still arrives — the alarm is the user's own and they are waiting for it. Only the name is
        // withheld, which is exactly what a deleted activity already produces.
        var body = document.RootElement.GetProperty("body").GetString();
        body.Should().NotBeNullOrWhiteSpace();
        body.Should().NotContain(foreignName, "the name belongs to another user's activity row");
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

        // Push configured, not EmailOff: quiet hours only defer the background channels, and the dispatcher
        // discounts one the deployment cannot deliver on (framework 240dc58) — with none, the control is never
        // deferred and the exemption would pass vacuously. No subscription is seeded; the assertions are on rows.
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: PushOn);

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
        byType[HistoryNotificationTypes.TimerBoundary].DeferredUntil
            .Should().BeNull("TimerBoundary is quiet-hours-exempt — the user scheduled this instant themselves");

        byType[NotificationType.Test].DeferredUntil
            .Should().NotBeNull("an ordinary kind is still deferred — the exemption is per-kind, not a switch-off");
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private Task<HttpResponseMessage> PostSessionAsync(HttpClient client, Guid sessionId, params DateTime[] boundaries) =>
        PostSessionAsync(client, sessionId, activityId: null, boundaries);

    private Task<HttpResponseMessage> PostSessionAsync(
        HttpClient client, Guid sessionId, long? activityId, params DateTime[] boundaries) =>
        client.PostAsJsonAsync(ScheduleRoute, new
        {
            SessionId = sessionId,
            ActivityId = activityId,
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

    /// <summary>
    /// An activity owned by a second <b>real</b> user — created through <c>UserManager</c>, like every other
    /// cross-user test here, so the row is the shape a real account produces.
    /// </summary>
    private async Task<long> SeedForeignActivityAsync(string name)
    {
        var otherUserId = await ActivityMergeTestSupport.SecondUserAsync(Fixture, $"timer-alarm-{Guid.NewGuid():N}@test.com");

        await using var db = CreateDbContext();
        return await SeedActivityAsync(db, otherUserId, name);
    }

    private async Task<long> SeedActivityAsync(DbContext db, long userId, string name)
    {
        var role = new ActivityRole { UserId = userId, Name = $"{name} Role {Guid.NewGuid():N}", Color = "#112233" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity { UserId = userId, Name = name, RoleId = role.Id };
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

    /// <summary>
    /// A scope with no logged user, which is what a scheduled job runs in: <c>ScopeUserId</c> resolves to
    /// null and the global user filter passes every row. The default fake reports
    /// <c>IsAuthenticated == true</c> unconditionally, so a background context cannot be simulated by
    /// leaving the principal out.
    /// </summary>
    private sealed class BackgroundLoggedUserService : ILoggedUserService
    {
        public ClaimsPrincipal? LoggedUserPrincipal => null;
        public bool IsAuthenticated => false;
        public IEnumerable<string> GetRoles => [];
        public string GetEmail => throw new InvalidOperationException("No logged user in a background context.");
        public long GetUserId => throw new InvalidOperationException("No logged user in a background context.");
        public long? GetUserIdOrNull => null;
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
