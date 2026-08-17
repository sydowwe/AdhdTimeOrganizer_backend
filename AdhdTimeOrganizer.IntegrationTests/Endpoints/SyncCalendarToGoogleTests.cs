using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.domain.extServiceContract.googleCalendar;
using FluentAssertions;
using Google.Apis.Calendar.v3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-3 / Section H — <c>SyncCalendarToGoogleEndpoint</c>. Per CQ-20 the endpoint's own try/catch only wraps
/// the per-task Google API loop, not the surrounding not-connected check, so the not-connected path is the one
/// worth pinning as clean rather than a 500. <c>IGoogleCalendarService</c> is stubbed via Moq throughout —
/// nothing here calls the real Google API.
/// <para>
/// The <c>GoogleApiException</c> 409/502 catch branches (revoked token vs. generic Google failure) need a
/// stubbed <c>CalendarService</c> whose underlying HTTP transport actually throws that exception type — that
/// requires wiring a custom <c>Google.Apis.Http.IHttpClientFactory</c>, which is out of scope for this pass;
/// noted here as a coverage gap rather than silently skipped.
/// </para>
/// </summary>
[Collection("Postgres")]
public class SyncCalendarToGoogleTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact(DisplayName = "Google Calendar not connected -> clean 400, not a 500")]
    public async Task NotConnected_ReturnsClean400()
    {
        var googleServiceMock = new Mock<IGoogleCalendarService>(MockBehavior.Strict);

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configureServices: services =>
        {
            services.RemoveAll<IGoogleCalendarService>();
            services.AddSingleton(googleServiceMock.Object);
        });

        long calendarId;
        await using (var db = CreateDbContext())
        {
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "google-sync-not-connected");
            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 7, 1));
            await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0));

            // The test user has no GoogleCalendarRefreshToken -- default/unset from AppDbContextFixture's seed.
            var user = await db.Set<User>().SingleAsync(u => u.Id == PlanningTestSeedHelper.TestUserId, CancellationToken);
            user.GoogleCalendarRefreshToken.Should().BeNull("the fixture's seeded user never connects Google Calendar");
        }

        var response = await factory.CreateClient().PostAsync($"api/calendar/{calendarId}/sync-to-google", null, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a clean, distinguishable 400 -- not an unhandled 500 -- for the common 'never connected' case");
        googleServiceMock.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "Syncing another user's calendar 404s")]
    public async Task OtherUsersCalendar_Returns404()
    {
        var googleServiceMock = new Mock<IGoogleCalendarService>(MockBehavior.Strict);

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configureServices: services =>
        {
            services.RemoveAll<IGoogleCalendarService>();
            services.AddSingleton(googleServiceMock.Object);
        });

        long theirCalendarId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            theirCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 7, 2), ReminderSeedHelper.OtherUserId);

            // The requesting user (TestUserId) must be "connected" here, or the not-connected 400 check would
            // short-circuit before the calendar lookup ever runs and this wouldn't be testing the IDOR path.
            var requester = await db.Set<User>().SingleAsync(u => u.Id == PlanningTestSeedHelper.TestUserId, CancellationToken);
            requester.GoogleCalendarRefreshToken = "fake-refresh-token";
            await db.SaveChangesAsync(CancellationToken);
        }

        var response = await factory.CreateClient().PostAsync($"api/calendar/{theirCalendarId}/sync-to-google", null, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        googleServiceMock.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "A connected user with an empty day syncs zero events, and the stubbed service sees no Insert/Update calls")]
    public async Task ConnectedWithNoTasks_SyncsZero_NeverInsertsOrUpdates()
    {
        // GetCalendarService is called unconditionally once the user is connected (before the per-task loop),
        // so it must be stubbed even for the empty-day case -- an inert real CalendarService that makes no
        // HTTP call unless Events.Insert/Update.ExecuteAsync is actually invoked, which an empty task list
        // never reaches.
        var inertCalendarService = new CalendarService(new Google.Apis.Services.BaseClientService.Initializer
        {
            ApplicationName = "AdhdTimeOrganizer-Test"
        });
        var googleServiceMock = new Mock<IGoogleCalendarService>(MockBehavior.Strict);
        googleServiceMock.Setup(s => s.GetCalendarService(It.IsAny<string>())).Returns(inertCalendarService);

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configureServices: services =>
        {
            services.RemoveAll<IGoogleCalendarService>();
            services.AddSingleton(googleServiceMock.Object);
        });

        long calendarId;
        await using (var db = CreateDbContext())
        {
            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 7, 3));

            var user = await db.Set<User>().SingleAsync(u => u.Id == PlanningTestSeedHelper.TestUserId, CancellationToken);
            user.GoogleCalendarRefreshToken = "fake-refresh-token";
            await db.SaveChangesAsync(CancellationToken);
        }

        var response = await factory.CreateClient().PostAsync($"api/calendar/{calendarId}/sync-to-google", null, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SyncResult>(JsonOpts, CancellationToken);
        body!.SyncedCount.Should().Be(0);

        googleServiceMock.Verify(s => s.GetCalendarService("fake-refresh-token"), Times.Once);
        googleServiceMock.VerifyNoOtherCalls();
    }

    private record SyncResult(int SyncedCount);
}
