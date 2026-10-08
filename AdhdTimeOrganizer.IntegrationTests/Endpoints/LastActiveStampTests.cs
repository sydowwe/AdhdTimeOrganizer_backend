using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.request.user;
using Sydowwe.Framework.application.dto.response.user;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// FO5 — who counts as "using the app", through the real sign-in pipeline rather than the test auth handler, which
/// mints no client type. A tracker's heartbeat is the user at their computer, not in the app: if it stamped
/// <c>LastActiveAt</c>, no user running the desktop tracker would ever be away, and the card would never appear.
/// </summary>
[Collection("Postgres")]
public class LastActiveStampTests(AppDbContextFixture fixture) : AuthTestBase(fixture)
{
    private const string Password = "Test@1234!";

    [Fact(DisplayName = "FO5: a tracker token's heartbeat does not stamp LastActiveAt; the web app's first request does")]
    public async Task TrackerHeartbeat_DoesNotStamp_WebRequestDoes()
    {
        var (userId, email) = await CreateRealUserAsync(Fixture, "presence", Password);

        using var tracker = CreateCookieClient();
        tracker.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TrackerTokenAsync(email));
        var heartbeat = await tracker.PostAsJsonAsync("activity-tracking/desktop/heartbeat", new DesktopActivityWindowDto
        {
            WindowStart = DateTime.UtcNow.Date.AddHours(DateTime.UtcNow.Hour),
            Entries =
            [
                new DesktopActivityEntryDto
                {
                    ProcessName = "presence.exe",
                    ProductName = "Presence",
                    WindowTitle = "Presence",
                    ExecutablePath = @"C:\presence.exe",
                    IsFullscreen = false,
                    ActiveSeconds = 30,
                    BackgroundSeconds = 0,
                    IsPlayingSound = false,
                    ActiveMonitor = 0
                }
            ]
        }, CancellationToken);
        heartbeat.StatusCode.Should().Be(HttpStatusCode.OK, "the tracker token must authenticate, or this proves nothing");

        (await LastActiveAtAsync(userId)).Should().BeNull();

        using var web = CreateCookieClient();
        await LoginAsync(web, email, Password);
        (await web.GetAsync("welcome-back", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent,
            "a user never measured before is stamped, not greeted");

        (await LastActiveAtAsync(userId)).Should().NotBeNull();
    }

    private async Task<string> TrackerTokenAsync(string email)
    {
        using var client = CreateCookieClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/extension/login");
        request.Headers.Add("X-Forwarded-For", Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(new ExtensionLoginRequest { Email = email, Password = Password });
        var response = await client.SendAsync(request, CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ExtensionLoginResponse>(CancellationToken))!.AccessToken;
    }

    private async Task<DateTime?> LastActiveAtAsync(long userId)
    {
        await using var db = CreateDbContext();
        return await db.Set<User>().IgnoreQueryFilters().Where(u => u.Id == userId)
            .Select(u => u.LastActiveAt).SingleAsync(CancellationToken);
    }
}
