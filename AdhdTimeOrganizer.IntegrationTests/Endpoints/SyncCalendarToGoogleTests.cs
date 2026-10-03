using System.Net;
using System.Net.Http.Json;
using System.Text;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.extServiceContract.googleCalendar;
using FluentAssertions;
using Google.Apis.Calendar.v3;
using Google.Apis.Http;
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
/// The task-filtering tests run a real <c>CalendarService</c> over <see cref="FakeGoogleCalendarTransport"/>, so
/// Insert/Update/Delete are exercised end to end without leaving the process. The <c>GoogleApiException</c>
/// 409/502 catch branches (revoked token vs. generic Google failure) could now be pinned the same way by having
/// the transport answer 401/500; that is still a coverage gap, noted rather than silently skipped.
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
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "google-sync-not-connected", ct: TestContext.Current.CancellationToken);
            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 7, 1), ct: TestContext.Current.CancellationToken);
            await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: TestContext.Current.CancellationToken);

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
            theirCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 7, 2), ReminderSeedHelper.OtherUserId, TestContext.Current.CancellationToken);

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
            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 7, 3), ct: TestContext.Current.CancellationToken);

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

    [Fact(DisplayName = "Cancelled and background tasks are not inserted; the rest are, and only they count as synced")]
    public async Task CancelledAndBackgroundTasks_AreNotInserted()
    {
        var google = new FakeGoogleCalendarTransport();
        await using var factory = CreateFactoryWith(google);

        long calendarId, plannedId, cancelledId, backgroundId;
        await using (var db = CreateDbContext())
        {
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "google-sync-filter", ct: TestContext.Current.CancellationToken);
            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 7, 4), ct: TestContext.Current.CancellationToken);
            plannedId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: TestContext.Current.CancellationToken);
            cancelledId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(10, 0), new TimeOnly(11, 0),
                status: PlannerTaskStatus.Cancelled, ct: TestContext.Current.CancellationToken);
            backgroundId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(11, 0), new TimeOnly(12, 0), ct: TestContext.Current.CancellationToken);

            var background = await db.Set<PlannerTask>().SingleAsync(t => t.Id == backgroundId, CancellationToken);
            background.IsBackground = true;
            await ConnectTestUserAsync(db);
        }

        var response = await factory.CreateClient().PostAsync($"api/calendar/{calendarId}/sync-to-google", null, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SyncResult>(JsonOpts, CancellationToken);
        body!.SyncedCount.Should().Be(1);
        body.RemovedCount.Should().Be(0);

        google.Requests.Should().ContainSingle().Which.Method.Should().Be(HttpMethod.Post, "only the one planned task is inserted");

        await using var verifyDb = CreateDbContext();
        var tasks = await verifyDb.Set<PlannerTask>().AsNoTracking()
            .Where(t => t.CalendarId == calendarId).ToDictionaryAsync(t => t.Id, CancellationToken);
        tasks[plannedId].GoogleEventId.Should().NotBeNull();
        tasks[cancelledId].GoogleEventId.Should().BeNull();
        tasks[backgroundId].GoogleEventId.Should().BeNull();
    }

    [Fact(DisplayName = "A task pushed earlier and since cancelled or made background has its Google event deleted and its id cleared")]
    public async Task PreviouslyPushedThenSkipped_DeletesGoogleEvent()
    {
        var google = new FakeGoogleCalendarTransport();
        await using var factory = CreateFactoryWith(google);

        long calendarId, cancelledId, backgroundId;
        await using (var db = CreateDbContext())
        {
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "google-sync-delete", ct: TestContext.Current.CancellationToken);
            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 7, 5), ct: TestContext.Current.CancellationToken);
            cancelledId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0),
                status: PlannerTaskStatus.Cancelled, ct: TestContext.Current.CancellationToken);
            backgroundId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(10, 0), new TimeOnly(11, 0), ct: TestContext.Current.CancellationToken);

            var cancelled = await db.Set<PlannerTask>().SingleAsync(t => t.Id == cancelledId, CancellationToken);
            cancelled.GoogleEventId = "evt-cancelled";
            var background = await db.Set<PlannerTask>().SingleAsync(t => t.Id == backgroundId, CancellationToken);
            background.GoogleEventId = "evt-background";
            background.IsBackground = true;
            await ConnectTestUserAsync(db);
        }

        // The user already deleted the background one by hand on Google — a 404 must not fail the sync.
        google.MissingEventIds.Add("evt-background");

        var response = await factory.CreateClient().PostAsync($"api/calendar/{calendarId}/sync-to-google", null, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SyncResult>(JsonOpts, CancellationToken);
        body!.SyncedCount.Should().Be(0);
        body.RemovedCount.Should().Be(2);

        google.Requests.Should().OnlyContain(r => r.Method == HttpMethod.Delete);
        google.Requests.Select(r => r.RequestUri!.AbsolutePath)
            .Should().BeEquivalentTo("/calendar/v3/calendars/primary/events/evt-cancelled", "/calendar/v3/calendars/primary/events/evt-background");

        await using var verifyDb = CreateDbContext();
        var ids = await verifyDb.Set<PlannerTask>().AsNoTracking()
            .Where(t => t.CalendarId == calendarId).Select(t => t.GoogleEventId).ToListAsync(CancellationToken);
        ids.Should().AllSatisfy(id => id.Should().BeNull("un-skipping the task later must insert a fresh event"));
    }

    private ITestClientFactory CreateFactoryWith(FakeGoogleCalendarTransport google)
    {
        var calendarService = new CalendarService(new Google.Apis.Services.BaseClientService.Initializer
        {
            ApplicationName = "AdhdTimeOrganizer-Test",
            HttpClientFactory = google
        });
        var googleServiceMock = new Mock<IGoogleCalendarService>(MockBehavior.Strict);
        googleServiceMock.Setup(s => s.GetCalendarService(It.IsAny<string>())).Returns(calendarService);

        return CreateFactory(TestRoles.AdminAndUser, configureServices: services =>
        {
            services.RemoveAll<IGoogleCalendarService>();
            services.AddSingleton(googleServiceMock.Object);
        });
    }

    private async Task ConnectTestUserAsync(DbContext db)
    {
        var user = await db.Set<User>().SingleAsync(u => u.Id == PlanningTestSeedHelper.TestUserId, CancellationToken);
        user.GoogleCalendarRefreshToken = "fake-refresh-token";
        await db.SaveChangesAsync(CancellationToken);
    }

    private record SyncResult(int SyncedCount, int RemovedCount);

    /// <summary>
    /// Stands in for Google's HTTP endpoint underneath a real <c>CalendarService</c>, so the endpoint's
    /// Insert/Update/Delete calls run for real and are recorded instead of leaving the process.
    /// </summary>
    private sealed class FakeGoogleCalendarTransport : HttpClientFactory
    {
        private int _nextId;

        public List<HttpRequestMessage> Requests { get; } = [];
        public HashSet<string> MissingEventIds { get; } = [];

        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => new Handler(this);

        private sealed class Handler(FakeGoogleCalendarTransport owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                owner.Requests.Add(request);

                var eventId = request.RequestUri!.Segments[^1];
                if (request.Method == HttpMethod.Delete)
                {
                    var status = owner.MissingEventIds.Contains(eventId) ? HttpStatusCode.NotFound : HttpStatusCode.NoContent;
                    return Task.FromResult(new HttpResponseMessage(status)
                    {
                        Content = status == HttpStatusCode.NotFound
                            ? new StringContent("""{"error":{"code":404,"message":"Not Found"}}""", Encoding.UTF8, "application/json")
                            : new StringContent("")
                    });
                }

                var id = request.Method == HttpMethod.Post ? $"evt-{Interlocked.Increment(ref owner._nextId)}" : eventId;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""{"id":"{{id}}"}""", Encoding.UTF8, "application/json")
                });
            }
        }
    }
}
