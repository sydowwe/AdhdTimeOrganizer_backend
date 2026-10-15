using AdhdTimeOrganizer.Tracking.application.notification;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.heartbeat;
using AdhdTimeOrganizer.Tracking.domain.helper.unified;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.infrastructure.jobs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.domain.entity;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// P2-04 — a tracker that stops reporting is told to its user, once per silence, and only while they are around. Both
/// halves fail silently: an ingest route that forgets to stamp makes its tracker look dead forever (or, never stamped,
/// invisible to the rule), and a sweep that does not mark repeats itself every hour. So every case asserts on the
/// <c>tracker_source_status</c> row and on what the notifier was handed; one runs the real pipeline and reads the
/// persisted notification.
/// <para>
/// <b>The clock is pinned to Tuesday 29 September 2020</b>, as in <c>MorningBriefingJobTests</c>: Quartz runs the sweep
/// hourly inside every test host on the <i>real</i> clock, and a stamp from 2020 is far past the rule's 30-day horizon
/// for it, so those fires never touch the rows under test. The fixture user is on UTC with the default 08:00 wake-up
/// and 00:00 bed time, so "10:00" below is both their wall clock and the instant.
/// </para>
/// </summary>
[Collection("Postgres")]
public class TrackerSilenceTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private static readonly string[] TrackingRoles = ["User", "ActivityTracking"];

    private static readonly DateOnly Tuesday = new(2020, 9, 29);

    private static DateTimeOffset At(int hour, int minute = 0, int dayOffset = 0) =>
        new(Tuesday.AddDays(dayOffset).ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero);

    // ---- test doubles -----------------------------------------------------------------------------

    private sealed class NotifierDouble : ITrackerSilenceNotifier
    {
        public List<(long UserId, TrackingSource Source, DateTime LastSeenAt)> Calls { get; } = [];

        /// <summary>Mirrors the real implementation swallowing a dispatch failure: recorded, and reported as not sent.</summary>
        public bool DispatchFails { get; set; }

        public Task<bool> NotifyAsync(long userId, TrackingSource source, DateTime lastSeenAt, CancellationToken ct)
        {
            Calls.Add((userId, source, lastSeenAt));
            return Task.FromResult(!DispatchFails);
        }
    }

    /// <summary>The real background scope has no user; see <c>RoutineSuggestedTodayJobTests</c>.</summary>
    private sealed class NoAmbientUserLoggedUserService : ILoggedUserService
    {
        public ClaimsPrincipal? LoggedUserPrincipal => null;
        public bool IsAuthenticated => false;
        public IEnumerable<string> GetRoles => [];
        public string GetEmail => throw new InvalidOperationException("No ambient user in a background job scope.");
        public long GetUserId => throw new InvalidOperationException("No ambient user in a background job scope.");
        public long? GetUserIdOrNull => null;
    }

    private sealed class MovableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    // ---- the sweep --------------------------------------------------------------------------------

    [Fact(DisplayName = "P2-04: a source silent for three days, its user in the app today — one notification, marked; a second run sends nothing")]
    public async Task SilentSource_PresentUser_IsNotifiedOnce()
    {
        var lastSeen = At(9, 0, dayOffset: -3).UtcDateTime;
        await SeedStatusAsync(TrackingSource.Desktop, lastSeen);
        await SetLastActiveAsync(At(8, 30).UtcDateTime);

        var notifier = new NotifierDouble();
        await RunSweepAsync(At(10, 0), notifier);

        notifier.Calls.Should().ContainSingle().Which.Should().Be((UserId, TrackingSource.Desktop, lastSeen));
        (await ReadStatusAsync(TrackingSource.Desktop)).SilenceNotifiedAt.Should().Be(At(10, 0).UtcDateTime);

        await RunSweepAsync(At(11, 0), notifier);
        await RunSweepAsync(At(10, 0, dayOffset: 1), notifier);
        notifier.Calls.Should().ContainSingle("one silence is one notification, however long it lasts");
    }

    [Fact(DisplayName = "P2-04: only a silence between 48 h and 30 days, of a user seen within a day, of an active account, counts")]
    public async Task Rule_Bounds()
    {
        await SetLastActiveAsync(At(8, 0).UtcDateTime);
        await SeedStatusAsync(TrackingSource.Desktop, At(11, 0, dayOffset: -2).UtcDateTime);      // 47 h: not yet
        await SeedStatusAsync(TrackingSource.WebExtension, At(9, 0, dayOffset: -31).UtcDateTime); // retired
        await SeedStatusAsync(TrackingSource.Android, At(9, 0, dayOffset: -2).UtcDateTime);       // 49 h: silent

        var notifier = new NotifierDouble();
        await RunSweepAsync(At(10, 0), notifier);

        notifier.Calls.Should().ContainSingle().Which.Source.Should().Be(TrackingSource.Android);
        (await ReadStatusAsync(TrackingSource.Desktop)).SilenceNotifiedAt.Should().BeNull();
        (await ReadStatusAsync(TrackingSource.WebExtension)).SilenceNotifiedAt.Should().BeNull();
    }

    [Fact(DisplayName = "P2-04: a user not seen in the app for a day hears nothing, and the silence stays armed for their return")]
    public async Task AbsentUser_IsNotNotified()
    {
        await SeedStatusAsync(TrackingSource.Desktop, At(9, 0, dayOffset: -3).UtcDateTime);
        await SetLastActiveAsync(At(9, 0, dayOffset: -2).UtcDateTime);

        var notifier = new NotifierDouble();
        await RunSweepAsync(At(10, 0), notifier);

        notifier.Calls.Should().BeEmpty();
        (await ReadStatusAsync(TrackingSource.Desktop)).SilenceNotifiedAt.Should().BeNull();

        // Never measured counts as absent too: there is no evidence anyone is there to read it.
        await SetLastActiveAsync(null);
        await RunSweepAsync(At(10, 0), notifier);
        notifier.Calls.Should().BeEmpty();

        await SetLastActiveAsync(At(9, 30).UtcDateTime);
        await RunSweepAsync(At(10, 0), notifier);
        notifier.Calls.Should().ContainSingle();
    }

    [Fact(DisplayName = "P2-04: the sweep waits for the user's waking day — nothing at 03:00, sent at the first run after wake-up")]
    public async Task Night_WaitsForWakeUp()
    {
        await SeedStatusAsync(TrackingSource.Desktop, At(1, 0, dayOffset: -3).UtcDateTime);
        await SetLastActiveAsync(At(0, 30).UtcDateTime);

        var notifier = new NotifierDouble();
        await RunSweepAsync(At(3, 0), notifier);
        await RunSweepAsync(At(7, 10), notifier);
        notifier.Calls.Should().BeEmpty("the default wake-up is 08:00");

        await RunSweepAsync(At(8, 10), notifier);
        notifier.Calls.Should().ContainSingle();
    }

    [Fact(DisplayName = "P2-04: a failed dispatch leaves the silence unmarked, and the next run retries")]
    public async Task FailedDispatch_IsRetried()
    {
        await SeedStatusAsync(TrackingSource.Android, At(9, 0, dayOffset: -3).UtcDateTime);
        await SetLastActiveAsync(At(9, 0).UtcDateTime);

        var notifier = new NotifierDouble { DispatchFails = true };
        await RunSweepAsync(At(10, 0), notifier);
        (await ReadStatusAsync(TrackingSource.Android)).SilenceNotifiedAt.Should().BeNull();

        notifier.DispatchFails = false;
        await RunSweepAsync(At(11, 0), notifier);

        notifier.Calls.Should().HaveCount(2);
        (await ReadStatusAsync(TrackingSource.Android)).SilenceNotifiedAt.Should().Be(At(11, 0).UtcDateTime);
    }

    [Fact(DisplayName = "P2-04: end to end — the bell row is a TrackerSilent naming the source by its wire name")]
    public async Task RealPipeline_PersistsTheNotification()
    {
        var lastSeen = At(9, 0, dayOffset: -3).UtcDateTime;
        await SeedStatusAsync(TrackingSource.WebExtension, lastSeen);
        await SetLastActiveAsync(At(9, 0).UtcDateTime);

        await RunSweepAsync(At(10, 0));

        await using var db = CreateDbContext();
        var notification = (await db.Set<Notification>().AsNoTracking()
                .Where(n => n.UserId == UserId && n.Type == TrackingNotificationTypes.TrackerSilent)
                .ToListAsync(CancellationToken))
            .Should().ContainSingle().Subject;

        var payload = JsonDocument.Parse(notification.PayloadJson).RootElement;
        payload.GetProperty("source").GetString().Should().Be(TrackingSourceNames.WebExtension);
        payload.GetProperty("lastSeenAt").GetDateTime().Should().Be(lastSeen);
        (await ReadStatusAsync(TrackingSource.WebExtension)).SilenceNotifiedAt.Should().NotBeNull();
    }

    // ---- the stamp --------------------------------------------------------------------------------

    /// <summary>
    /// The guard on the ingest half: each of the three routes must stamp <i>its own</i> source. One that forgot would
    /// leave its tracker unstamped — never silent, never shown as reporting — and nothing else notices.
    /// </summary>
    [Fact(DisplayName = "P2-04: each ingest route stamps its own source, and the unified sources row reports it")]
    public async Task EveryIngestRoute_StampsItsSource()
    {
        var clock = new MovableTimeProvider(At(10, 0));
        await using var factory = TrackingFactory(clock);
        using var client = factory.CreateClient();

        (await client.PostAsJsonAsync("/api/activity-tracking/desktop/heartbeat", DesktopHeartbeat(), CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/activity-tracking/web-extension/heartbeat", WebExtensionHeartbeat(), CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/activity-tracking/android/sync", AndroidSync(), CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var db = CreateDbContext())
        {
            var rows = await db.Set<TrackerSourceStatus>().IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.UserId == UserId)
                .ToListAsync(CancellationToken);

            rows.Select(r => r.Source).Should().BeEquivalentTo(TrackingSourceNames.All);
            rows.Should().AllSatisfy(r =>
            {
                r.LastSeenAt.Should().Be(At(10, 0).UtcDateTime);
                r.SilenceNotifiedAt.Should().BeNull();
            });
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var response = await client.PostAsJsonAsync("/api/activity-tracking/unified/sources", new
        {
            dateFrom = today,
            dateTo = today,
            from = new { hours = 0, minutes = 0 },
            to = new { hours = 0, minutes = 0 },
            sources = new[] { "webExtension", "desktop", "android" }
        }, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var sources = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        sources.EnumerateArray().Should().HaveCount(3).And.AllSatisfy(s =>
            s.GetProperty("lastSeenAt").GetDateTime().Should().Be(At(10, 0).UtcDateTime));
    }

    [Fact(DisplayName = "P2-04: the stamp is throttled — two syncs a minute apart write once; an hour later moves it")]
    public async Task Stamp_IsThrottled()
    {
        var clock = new MovableTimeProvider(At(10, 0));
        await using (var factory = TrackingFactory(clock))
        {
            using var client = factory.CreateClient();

            await client.PostAsJsonAsync("/api/activity-tracking/android/sync", AndroidSync(), CancellationToken);
            clock.Now = At(10, 1);
            await client.PostAsJsonAsync("/api/activity-tracking/android/sync", AndroidSync(), CancellationToken);
        }

        (await ReadStatusAsync(TrackingSource.Android)).LastSeenAt.Should().Be(At(10, 0).UtcDateTime,
            "a heartbeat a minute later is not worth a write");

        // The in-memory throttle expires on the real clock, so "an hour later" is a fresh host here — which is also the
        // case that leans on the conditional UPDATE alone.
        await using (var later = TrackingFactory(new MovableTimeProvider(At(11, 1))))
        {
            using var client = later.CreateClient();
            await client.PostAsJsonAsync("/api/activity-tracking/android/sync", AndroidSync(), CancellationToken);
        }

        (await ReadStatusAsync(TrackingSource.Android)).LastSeenAt.Should().Be(At(11, 1).UtcDateTime);
    }

    /// <summary>
    /// The throttle's second layer: a fresh process has an empty in-memory cache, and must still not move a stamp
    /// another instance wrote a minute ago.
    /// </summary>
    [Fact(DisplayName = "P2-04: a fresh stamp is not moved even by a process whose throttle cache is empty")]
    public async Task Stamp_FreshRow_IsNotMovedAcrossProcesses()
    {
        await SeedStatusAsync(TrackingSource.Desktop, At(9, 59).UtcDateTime);

        await using var factory = TrackingFactory(new MovableTimeProvider(At(10, 0)));
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/activity-tracking/desktop/heartbeat", DesktopHeartbeat(), CancellationToken);

        (await ReadStatusAsync(TrackingSource.Desktop)).LastSeenAt.Should().Be(At(9, 59).UtcDateTime);
    }

    [Fact(DisplayName = "P2-04: a new report clears the mark, so the next silence is told again")]
    public async Task NewReport_ClearsTheMark()
    {
        await SeedStatusAsync(TrackingSource.Desktop, At(9, 0, dayOffset: -3).UtcDateTime, silenceNotifiedAt: At(10, 0, dayOffset: -1).UtcDateTime);

        await using (var factory = TrackingFactory(new MovableTimeProvider(At(10, 0))))
        {
            using var client = factory.CreateClient();
            (await client.PostAsJsonAsync("/api/activity-tracking/desktop/heartbeat", DesktopHeartbeat(), CancellationToken))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var status = await ReadStatusAsync(TrackingSource.Desktop);
        status.LastSeenAt.Should().Be(At(10, 0).UtcDateTime);
        status.SilenceNotifiedAt.Should().BeNull();

        // Quiet again for three days, the user in the app: told again.
        await SetLastActiveAsync(At(9, 0, dayOffset: 3).UtcDateTime);
        var notifier = new NotifierDouble();
        await RunSweepAsync(At(11, 0, dayOffset: 3), notifier);
        notifier.Calls.Should().ContainSingle();
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private ITestClientFactory TrackingFactory(TimeProvider clock) =>
        CreateFactory(TrackingRoles, UserId, configureServices: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(clock);
        });

    /// <summary>Runs the sweep the way the Scheduler dispatcher does — one scope per fire, no ambient user, clock pinned.</summary>
    private async Task RunSweepAsync(DateTimeOffset now, NotifierDouble? notifier = null)
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configureServices: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new MovableTimeProvider(now));

            services.RemoveAll<ILoggedUserService>();
            services.AddScoped<ILoggedUserService>(_ => new NoAmbientUserLoggedUserService());

            if (notifier is not null)
            {
                services.RemoveAll<ITrackerSilenceNotifier>();
                services.AddSingleton<ITrackerSilenceNotifier>(notifier);
            }
        });

        await using var scope = factory.Services.CreateAsyncScope();
        var handler = (IScheduledJobHandler)ActivatorUtilities.CreateInstance(scope.ServiceProvider, typeof(TrackerSilenceJobHandler));

        await handler.ExecuteAsync(new ScheduledJobContext
        {
            ScheduledFireTimeUtc = now.UtcDateTime,
            ActualFireTimeUtc = now.UtcDateTime,
            JobKey = handler.Key,
            CorrelationId = "tracker-silence-test",
            TriggerSource = TriggerSource.Manual
        }, CancellationToken);
    }

    private async Task SeedStatusAsync(TrackingSource source, DateTime lastSeenAt, DateTime? silenceNotifiedAt = null)
    {
        await using var db = CreateDbContext();
        db.Set<TrackerSourceStatus>().Add(new TrackerSourceStatus
        {
            UserId = UserId,
            Source = source,
            LastSeenAt = lastSeenAt,
            SilenceNotifiedAt = silenceNotifiedAt
        });
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task<TrackerSourceStatus> ReadStatusAsync(TrackingSource source)
    {
        await using var db = CreateDbContext();
        return await db.Set<TrackerSourceStatus>().IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(s => s.UserId == UserId && s.Source == source, CancellationToken);
    }

    private async Task SetLastActiveAsync(DateTime? lastActiveAt)
    {
        await using var db = CreateDbContext();
        await db.Set<User>().IgnoreQueryFilters().Where(u => u.Id == UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastActiveAt, lastActiveAt), CancellationToken);
    }

    // The ingest validators bound these against the real clock, which is independent of the pinned one the stamp reads.

    private static DateTime CurrentMinute()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
    }

    private static DesktopActivityWindowDto DesktopHeartbeat() => new()
    {
        WindowStart = CurrentMinute(),
        Entries =
        [
            new DesktopActivityEntryDto
            {
                ProcessName = "code.exe",
                ProductName = "Code",
                WindowTitle = $"silence-{Guid.NewGuid():N}",
                ExecutablePath = @"C:\code.exe",
                IsFullscreen = false,
                ActiveSeconds = 30,
                BackgroundSeconds = 0,
                IsPlayingSound = false,
                ActiveMonitor = 0
            }
        ]
    };

    private static WebExtensionHeartbeatRequest WebExtensionHeartbeat() => new()
    {
        HeartbeatAt = DateTime.UtcNow,
        WindowStart = CurrentMinute(),
        WindowMinutes = 1,
        IsFinal = false,
        Activities = [new WebExtensionEntryDto { Domain = "example.com", Url = "https://example.com", ActiveSeconds = 30, BackgroundSeconds = 0 }]
    };

    private static AndroidSyncRequest AndroidSync() => new()
    {
        DeviceId = "pixel-8",
        SyncedUpToUtc = DateTime.UtcNow,
        Sessions = []
    };
}
