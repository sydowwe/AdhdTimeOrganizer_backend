using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.heartbeat;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.application.dto.request.user;
using Sydowwe.Framework.application.dto.response.user;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

[Collection("Postgres")]
public class ExtensionActivityTrackingTests(AppDbContextFixture fixture) : AuthTestBase(fixture)
{
    private const string Password = "Test@1234!";

    [Fact]
    public async Task ExtensionLogin_WithExtensionAccess_ReturnsTokens()
    {
        var email = "extension-user@test.com";
        await CreateTrackerUserAsync(email);

        var response = await ExtensionLoginAsync(email);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ExtensionLoginResponse>( TestContext.Current.CancellationToken);
        result.Should().NotBeNull();
        result!.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.RequiresTwoFactor.Should().BeFalse();
    }

    /// <summary>
    /// Every account may sign a tracker in (FO7, 2026-10-06). The host used to gate on a <c>User.HasExtensionAccess</c>
    /// column that only seeders ever set, so every tracker worked for the root admin alone; the column has since been
    /// dropped. The one refusal left is an account scheduled for deletion (P3-07, <c>AccountDeletionGraceTests</c>).
    /// </summary>
    [Fact]
    public async Task ExtensionLogin_AnyAccount_ReturnsTokens()
    {
        var email = "no-extension-user@test.com";
        await CreateTrackerUserAsync(email);

        var response = await ExtensionLoginAsync(email);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ExtensionLoginResponse>(TestContext.Current.CancellationToken);
        result!.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Heartbeat_WithExtensionToken_ReturnsOk()
    {
        var email = "heartbeat-user@test.com";
        await CreateTrackerUserAsync(email);

        var loginResult = await ExtensionLoginSuccessAsync(email);

        using var extensionClient = CreateCookieClient();
        extensionClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.AccessToken);

        var windowStart = DateTime.UtcNow.Date.AddHours(DateTime.UtcNow.Hour).AddMinutes(DateTime.UtcNow.Minute);
        var request = new DesktopActivityWindowDto
        {
            WindowStart = windowStart,
            Entries = new List<DesktopActivityEntryDto>
            {
                new()
                {
                    ProcessName = "test.exe",
                    ProductName = "Test Product",
                    WindowTitle = "Test Window",
                    ExecutablePath = @"C:\test\test.exe",
                    IsFullscreen = false,
                    ActiveSeconds = 30,
                    BackgroundSeconds = 0,
                    IsPlayingSound = false,
                    ActiveMonitor = 0
                }
            }
        };

        var response = await extensionClient.PostAsJsonAsync("activity-tracking/desktop/heartbeat", request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AndroidSync_WithExtensionToken_ReturnsOk()
    {
        var email = "android-user@test.com";
        await CreateTrackerUserAsync(email);

        var loginResult = await ExtensionLoginSuccessAsync(email);

        using var extensionClient = CreateCookieClient();
        extensionClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.AccessToken);

        var request = new AndroidSyncRequest
        {
            DeviceId = "test-device",
            SyncedUpToUtc = DateTime.UtcNow,
            Sessions = new List<AndroidSessionItemDto>()
        };

        var response = await extensionClient.PostAsJsonAsync("activity-tracking/android/sync", request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Heartbeat_WithExtensionToken_ButNoActivityTrackingRole_Returns403()
    {
        var email = "no-role-user@test.com";
        await CreateTrackerUserAsync(email, hasRole: false);

        var loginResult = await ExtensionLoginSuccessAsync(email);

        using var extensionClient = CreateCookieClient();
        extensionClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.AccessToken);

        var request = new DesktopActivityWindowDto
        {
            WindowStart = DateTime.UtcNow,
            Entries = new List<DesktopActivityEntryDto>()
        };

        var response = await extensionClient.PostAsJsonAsync("activity-tracking/desktop/heartbeat", request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WebJwt_CannotAccess_ActivityTrackingPolicy_Returns403()
    {
        // web-authenticated via cookie JWT (no ActivityTracking role in claims)
        using var webClient = CreateCookieClient();
        await LoginAsync(webClient, TestEmail, TestPassword);

        var request = new DesktopActivityWindowDto
        {
            WindowStart = DateTime.UtcNow,
            Entries = new List<DesktopActivityEntryDto>()
        };

        var response = await webClient.PostAsJsonAsync("activity-tracking/desktop/heartbeat", request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// The web-extension heartbeat is the third ingest route, and it is gated the same way as the
    /// desktop and android ones. <c>[AllowExtensionClients]</c> alone only lifts the blanket
    /// <c>DenyExtensionClients</c> policy — it grants nothing — so without an explicit
    /// <c>Policies(ActivityTracking)</c> the route silently falls back to the configurator's
    /// <c>Roles("User","Admin","Root")</c> and any web session can post browsing rows into the ledger.
    /// Nothing about that shows up as a build error or a log line, hence these three cases.
    /// </summary>
    [Fact]
    public async Task WebExtensionHeartbeat_WithExtensionToken_ReturnsOk()
    {
        var email = "web-heartbeat-user@test.com";
        await CreateTrackerUserAsync(email);

        var loginResult = await ExtensionLoginSuccessAsync(email);

        using var extensionClient = CreateCookieClient();
        extensionClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.AccessToken);

        var response = await extensionClient.PostAsJsonAsync("activity-tracking/web-extension/heartbeat",
            WebExtensionHeartbeat(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WebExtensionHeartbeat_WithExtensionToken_ButNoActivityTrackingRole_Returns403()
    {
        var email = "web-heartbeat-no-role-user@test.com";
        await CreateTrackerUserAsync(email, hasRole: false);

        var loginResult = await ExtensionLoginSuccessAsync(email);

        using var extensionClient = CreateCookieClient();
        extensionClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.AccessToken);

        var response = await extensionClient.PostAsJsonAsync("activity-tracking/web-extension/heartbeat",
            WebExtensionHeartbeat(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WebJwt_CannotAccess_WebExtensionHeartbeat_Returns403()
    {
        using var webClient = CreateCookieClient();
        await LoginAsync(webClient, TestEmail, TestPassword);

        var response = await webClient.PostAsJsonAsync("activity-tracking/web-extension/heartbeat",
            WebExtensionHeartbeat(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// A minimal request the validator accepts: one whole-minute window, one entry inside the 0-60
    /// second bounds.
    /// </summary>
    private static WebExtensionHeartbeatRequest WebExtensionHeartbeat()
    {
        var now = DateTime.UtcNow;
        return new WebExtensionHeartbeatRequest
        {
            HeartbeatAt = now,
            WindowStart = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc),
            WindowMinutes = 1,
            IsFinal = false,
            Activities =
            [
                new WebExtensionEntryDto
                {
                    Domain = "example.com",
                    Url = "https://example.com/page",
                    ActiveSeconds = 30,
                    BackgroundSeconds = 0
                }
            ]
        };
    }

    [Fact]
    public async Task ExtensionJwt_CannotAccess_WebOnlyEndpoint_Returns403()
    {
        var email = "extension-webonly-test@test.com";
        await CreateTrackerUserAsync(email);

        var loginResult = await ExtensionLoginSuccessAsync(email);

        using var extensionClient = CreateCookieClient();
        extensionClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.AccessToken);

        // user/sessions uses the fallback policy which blocks extension clients via ExtensionClientRequirement
        var response = await extensionClient.GetAsync("user/sessions", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// A tracker token may only write tracking data, and the notification hub reads user data: every live
    /// notification's title and body. The hub is not a FastEndpoints route, so the configurator's blanket
    /// <c>DenyExtensionClients</c> never reaches it, and its own <c>[Authorize]</c> keeps the fallback policy
    /// off it too. Until <c>Program.cs</c> attached the deny to the hub mapping, a tracker token connected.
    /// 403 rather than 401: the token authenticates, and is refused for being a tracker's.
    /// </summary>
    [Fact]
    public async Task ExtensionJwt_CannotOpen_TheNotificationHub()
    {
        var email = "extension-hub@test.com";
        await CreateTrackerUserAsync(email);
        var loginResult = await ExtensionLoginSuccessAsync(email);

        var server = ((WebApplicationFactory<Program>)Fixture.UnauthenticatedFactory).Server;
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "/hubs/notifications"), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.WebSocketFactory = async (context, ct) =>
                {
                    var handshakeUri = new UriBuilder(context.Uri) { Scheme = Uri.UriSchemeHttp }.Uri;
                    var webSocketClient = server.CreateWebSocketClient();
                    // How a tracker authenticates: its access token as a bearer header.
                    webSocketClient.ConfigureRequest = request =>
                        request.Headers.Authorization = $"Bearer {loginResult.AccessToken}";
                    return await webSocketClient.ConnectAsync(handshakeUri, ct);
                };
            })
            .Build();

        var start = () => connection.StartAsync(CancellationToken);

        (await start.Should().ThrowAsync<Exception>()).Which.Message.Should().Contain("403");
    }

    /// <summary>
    /// The wiring half of the Tracking seam, end to end over HTTP: a heartbeat entry that a pattern
    /// mapping resolves to an activity must (a) land in <c>ActivityHistory</c> — which Tracking no
    /// longer writes itself, it goes through Core's <c>IActivityTimeAttributionSink</c> that History
    /// implements — and (b) drive planner-task completion through
    /// <c>ActivityTimeRecordedEvent</c>.
    /// <para>
    /// Both halves fail <b>silently</b> if broken. An unregistered sink throws at activation (loud, by
    /// design), but a handler FastEndpoints never discovered, or an event that is built and never
    /// published, simply leaves the planner task untouched with a 200 on the wire. The branch matrix
    /// behind (b) lives in <c>ActivityTimeAutomationTests</c>; this case exists to prove the two ends
    /// are connected at all.
    /// </para>
    /// <para>
    /// It also happens to be the only test that ever noticed the global user query filter baking the
    /// first caller's id into the cached SQL — it signs in as a real user, so it read user 999's rows
    /// whenever <c>ActivityTimeAutomationTests</c> compiled the shapes first, and passed in isolation.
    /// That is now pinned deterministically by <c>UserScopingQueryFilterTests</c>; if this one starts
    /// failing on <c>Status</c> again, check there before suspecting the event wiring.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Heartbeat_MappedEntry_AttributesTimeAndCompletesPlannerTask()
    {
        var email = "heartbeat-automation@test.com";
        await CreateTrackerUserAsync(email);

        long userId;
        long activityId;
        long plannerTaskId;
        await using (var db = CreateDbContext())
        {
            userId = (await db.Set<User>().FirstAsync(u => u.Email == email, CancellationToken)).Id;

            var role = new ActivityRole { UserId = userId, Name = "Automation role", Color = "#0f0f0f" };
            db.Set<ActivityRole>().Add(role);
            await db.SaveChangesAsync(CancellationToken);

            var activity = new Activity { UserId = userId, Name = "Automated activity", RoleId = role.Id };
            db.Set<Activity>().Add(activity);
            await db.SaveChangesAsync(CancellationToken);
            activityId = activity.Id;

            // Exact-match on the process name the heartbeat below reports, so the entry attributes.
            db.Set<TrackerDesktopMappingByPattern>().Add(new TrackerDesktopMappingByPattern
            {
                UserId = userId,
                ProcessName = "automated.exe",
                ProcessNameMatchType = PatternMatchType.Exact,
                IsActive = true,
                ActivityId = activityId
            });

            var day = DateOnly.FromDateTime(DateTime.UtcNow);
            var calendar = new Calendar
            {
                UserId = userId,
                Date = day,
                DayType = DayType.Workday,
                WakeUpTime = new TimeOnly(7, 0),
                BedTime = new TimeOnly(23, 0)
            };
            db.Set<Calendar>().Add(calendar);
            await db.SaveChangesAsync(CancellationToken);

            // 5 planned minutes against the five one-minute heartbeats below, so the threshold is met
            // exactly. A heartbeat window is one minute (DesktopActivityHeartbeatValidator caps the
            // entries at 60 seconds), so crossing a multi-minute threshold takes several of them.
            var task = new PlannerTask
            {
                UserId = userId,
                ActivityId = activityId,
                CalendarId = calendar.Id,
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(9, 5),
                IsBackground = false,
                Status = PlannerTaskStatus.NotStarted
            };
            db.Set<PlannerTask>().Add(task);
            await db.SaveChangesAsync(CancellationToken);
            plannerTaskId = task.Id;
        }

        var loginResult = await ExtensionLoginSuccessAsync(email);
        using var extensionClient = CreateCookieClient();
        extensionClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.AccessToken);

        // Five contiguous windows: each one starts exactly where the previous ended, which is what makes
        // the sink extend the single ActivityHistory row rather than emit one row per heartbeat.
        var firstWindowStart = DateTime.UtcNow.Date.AddHours(9);
        for (var minute = 0; minute < 5; minute++)
        {
            var response = await extensionClient.PostAsJsonAsync("activity-tracking/desktop/heartbeat", new DesktopActivityWindowDto
            {
                WindowStart = firstWindowStart.AddMinutes(minute),
                Entries =
                [
                    new DesktopActivityEntryDto
                    {
                        ProcessName = "automated.exe",
                        ProductName = "Automated",
                        WindowTitle = "Automated",
                        ExecutablePath = @"C:\a\automated.exe",
                        IsFullscreen = false,
                        ActiveSeconds = 60,
                        BackgroundSeconds = 0,
                        IsPlayingSound = false,
                        ActiveMonitor = 0
                    }
                ]
            }, TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await using var assertDb = CreateDbContext();

        var attributed = await assertDb.Set<ActivityHistory>()
            .IgnoreQueryFilters()
            .Where(h => h.UserId == userId && h.ActivityId == activityId)
            .ToListAsync(CancellationToken);
        attributed.Should().ContainSingle("the heartbeat must attribute through IActivityTimeAttributionSink")
            .Which.Length.TotalSeconds.Should().Be(300);

        var plannerTask = await assertDb.Set<PlannerTask>()
            .IgnoreQueryFilters()
            .FirstAsync(t => t.Id == plannerTaskId, CancellationToken);
        plannerTask.Status.Should().Be(PlannerTaskStatus.Completed,
            "ActivityTimeRecordedEvent must reach ActivityTimeRecordedEventHandler -- if this is still " +
            "NotStarted the event was never published or the handler was never discovered");
    }

    /// <summary>
    /// bugs-to-investigate #1: the heartbeat's "day" is the user's, not UTC's. The windows below are posted
    /// at 23:50Z yesterday for a user whose zone is at least two hours ahead — already <i>today</i> on their
    /// clock. The heartbeat used to take <c>DateOnly.FromDateTime(WindowStart)</c>, which drove the task on
    /// yesterday's calendar row and left today's open; for a Bratislava user that is every minute tracked
    /// between their midnight and 01:00/02:00.
    /// <para>
    /// Both days carry an open task for the activity, so the assertions tell "the right day moved" from
    /// "some day moved". The zone is picked at run time, like
    /// <c>PlanningTestSeedHelper.UseZoneOffsetFromUtcTodayAsync</c>, because zone ids differ by platform.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Heartbeat_AfterTheUsersMidnight_DrivesTheUsersTodayNotUtcs()
    {
        var email = "heartbeat-user-day@test.com";
        await CreateTrackerUserAsync(email);

        var utcToday = DateOnly.FromDateTime(DateTime.UtcNow);
        var firstWindowStart = utcToday.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddMinutes(-10);
        var zone = TimeZoneInfo.GetSystemTimeZones()
            .First(z => z.GetUtcOffset(firstWindowStart) >= TimeSpan.FromHours(2) && z.GetUtcOffset(firstWindowStart) <= TimeSpan.FromHours(12));
        var localWindowStart = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(firstWindowStart, zone));

        long userId, userDayTaskId, utcDayTaskId;
        await using (var db = CreateDbContext())
        {
            var user = await db.Set<User>().FirstAsync(u => u.Email == email, CancellationToken);
            user.Timezone = zone;
            userId = user.Id;
            await db.SaveChangesAsync(CancellationToken);

            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "User-day activity", userId, CancellationToken);
            db.Set<TrackerDesktopMappingByPattern>().Add(new TrackerDesktopMappingByPattern
            {
                UserId = userId,
                ProcessName = "userday.exe",
                ProcessNameMatchType = PatternMatchType.Exact,
                IsActive = true,
                ActivityId = activityId
            });
            await db.SaveChangesAsync(CancellationToken);

            // Today on the user's clock: the five minutes the windows below cover, as they read there.
            var userDayCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, utcToday, userId, CancellationToken);
            userDayTaskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(
                db, activityId, userDayCalendarId, localWindowStart, localWindowStart.AddMinutes(5), userId, ct: CancellationToken);

            // Yesterday on the user's clock — but today's date by UTC's reading of the window.
            var utcDayCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, utcToday.AddDays(-1), userId, CancellationToken);
            utcDayTaskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(
                db, activityId, utcDayCalendarId, new TimeOnly(23, 50), new TimeOnly(23, 55), userId, ct: CancellationToken);
        }

        var loginResult = await ExtensionLoginSuccessAsync(email);
        using var extensionClient = CreateCookieClient();
        extensionClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.AccessToken);

        for (var minute = 0; minute < 5; minute++)
        {
            var response = await extensionClient.PostAsJsonAsync("activity-tracking/desktop/heartbeat", new DesktopActivityWindowDto
            {
                WindowStart = firstWindowStart.AddMinutes(minute),
                Entries =
                [
                    new DesktopActivityEntryDto
                    {
                        ProcessName = "userday.exe",
                        ProductName = "User day",
                        WindowTitle = "User day",
                        ExecutablePath = @"C:\a\userday.exe",
                        IsFullscreen = false,
                        ActiveSeconds = 60,
                        BackgroundSeconds = 0,
                        IsPlayingSound = false,
                        ActiveMonitor = 0
                    }
                ]
            }, TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await using var assertDb = CreateDbContext();
        var userDayTask = await assertDb.Set<PlannerTask>().IgnoreQueryFilters()
            .FirstAsync(t => t.Id == userDayTaskId, CancellationToken);
        userDayTask.Status.Should().Be(PlannerTaskStatus.Completed,
            "the windows fall on the user's today, and five tracked minutes meet a five-minute block");
        userDayTask.ActualEndTime.Should().Be(localWindowStart.AddMinutes(5),
            "actual times are written on the user's clock, the clock the task's own span is on");

        (await assertDb.Set<PlannerTask>().IgnoreQueryFilters().FirstAsync(t => t.Id == utcDayTaskId, CancellationToken))
            .Status.Should().Be(PlannerTaskStatus.NotStarted,
                "UTC's date for the window is the user's yesterday, which the tracked time has no business touching");
    }

    private async Task<ExtensionLoginResponse> ExtensionLoginSuccessAsync(string email)
    {
        var response = await ExtensionLoginAsync(email);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ExtensionLoginResponse>();
        result.Should().NotBeNull();
        return result!;
    }

    private async Task<HttpResponseMessage> ExtensionLoginAsync(string email)
    {
        using var client = CreateCookieClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/extension/login");
        request.Headers.Add("X-Forwarded-For", Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(new ExtensionLoginRequest { Email = email, Password = Password });
        return await client.SendAsync(request);
    }

    private async Task CreateTrackerUserAsync(string email, bool hasRole = true)
    {
        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<UserRole>>();

        var user = await userManager.FindByEmailAsync(email);
        if (user != null)
            return;

        user = new User
        {
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            Locale = AvailableLocales.En,
            Timezone = TimeZoneInfo.Utc
        };

        await userManager.CreateAsync(user, Password);

        string[] roles = hasRole ? ["User"] : [];
        foreach (var role in roles)
        {
            if (await roleManager.FindByNameAsync(role) == null)
                await roleManager.CreateAsync(new UserRole
                {
                    Name = role,
                    Description = role,
                    IsDefault = false,
                    RoleLevel = 1,
                    IsAssignable = true
                });

            await userManager.AddToRoleAsync(user, role);
        }
    }
}