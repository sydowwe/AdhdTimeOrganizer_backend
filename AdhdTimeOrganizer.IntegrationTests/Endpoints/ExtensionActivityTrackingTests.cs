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
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
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
        await CreateUserWithExtensionAccess(email, true);

        var response = await ExtensionLoginAsync(email);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ExtensionLoginResponse>();
        result.Should().NotBeNull();
        result!.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.RequiresTwoFactor.Should().BeFalse();
    }

    [Fact]
    public async Task ExtensionLogin_WithoutExtensionAccess_Returns403()
    {
        var email = "no-extension-user@test.com";
        await CreateUserWithExtensionAccess(email, false);

        var response = await ExtensionLoginAsync(email);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Heartbeat_WithExtensionToken_ReturnsOk()
    {
        var email = "heartbeat-user@test.com";
        await CreateUserWithExtensionAccess(email, true);

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

        var response = await extensionClient.PostAsJsonAsync("activity-tracking/desktop/heartbeat", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AndroidSync_WithExtensionToken_ReturnsOk()
    {
        var email = "android-user@test.com";
        await CreateUserWithExtensionAccess(email, true);

        var loginResult = await ExtensionLoginSuccessAsync(email);

        using var extensionClient = CreateCookieClient();
        extensionClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.AccessToken);

        var request = new AndroidSyncRequest
        {
            DeviceId = "test-device",
            SyncedUpToUtc = DateTime.UtcNow,
            Sessions = new List<AndroidSessionItemDto>()
        };

        var response = await extensionClient.PostAsJsonAsync("activity-tracking/android/sync", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Heartbeat_WithExtensionToken_ButNoActivityTrackingRole_Returns403()
    {
        var email = "no-role-user@test.com";
        await CreateUserWithExtensionAccess(email, true, false);

        var loginResult = await ExtensionLoginSuccessAsync(email);

        using var extensionClient = CreateCookieClient();
        extensionClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.AccessToken);

        var request = new DesktopActivityWindowDto
        {
            WindowStart = DateTime.UtcNow,
            Entries = new List<DesktopActivityEntryDto>()
        };

        var response = await extensionClient.PostAsJsonAsync("activity-tracking/desktop/heartbeat", request);

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

        var response = await webClient.PostAsJsonAsync("activity-tracking/desktop/heartbeat", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ExtensionJwt_CannotAccess_WebOnlyEndpoint_Returns403()
    {
        var email = "extension-webonly-test@test.com";
        await CreateUserWithExtensionAccess(email, true);

        var loginResult = await ExtensionLoginSuccessAsync(email);

        using var extensionClient = CreateCookieClient();
        extensionClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.AccessToken);

        // user/sessions uses the fallback policy which blocks extension clients via ExtensionClientRequirement
        var response = await extensionClient.GetAsync("user/sessions");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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
    /// </summary>
    [Fact]
    public async Task Heartbeat_MappedEntry_AttributesTimeAndCompletesPlannerTask()
    {
        var email = "heartbeat-automation@test.com";
        await CreateUserWithExtensionAccess(email, true);

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
            });

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

    private async Task CreateUserWithExtensionAccess(string email, bool hasExtensionAccess, bool hasRole = true)
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
            HasExtensionAccess = hasExtensionAccess,
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