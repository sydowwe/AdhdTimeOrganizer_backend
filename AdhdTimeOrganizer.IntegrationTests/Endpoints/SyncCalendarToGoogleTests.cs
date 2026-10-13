using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.entity.reminder;
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

    [Fact(DisplayName = "A connected user with an empty day syncs zero events and writes nothing to Google")]
    public async Task ConnectedWithNoTasks_SyncsZero_WritesNothing()
    {
        var google = new FakeGoogleCalendarTransport();
        await using var factory = CreateFactoryWith(google);

        long calendarId;
        await using (var db = CreateDbContext())
        {
            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 7, 3), ct: TestContext.Current.CancellationToken);
            await ConnectTestUserAsync(db);
        }

        var response = await PostSyncAsync(factory, calendarId);

        response.SyncedCount.Should().Be(0);
        response.RemovedCount.Should().Be(0);
        google.Writes.Should().BeEmpty("an empty day is still listed, for orphans, but nothing is written");
        google.Requests.Should().ContainSingle().Which.Method.Should().Be(HttpMethod.Get);
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

        google.Writes.Should().ContainSingle().Which.Method.Should().Be(HttpMethod.Post, "only the one planned task is inserted");

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

        google.Writes.Should().OnlyContain(r => r.Method == HttpMethod.Delete);
        google.Writes.Select(r => r.RequestUri!.AbsolutePath)
            .Should().BeEquivalentTo("/calendar/v3/calendars/primary/events/evt-cancelled", "/calendar/v3/calendars/primary/events/evt-background");

        await using var verifyDb = CreateDbContext();
        var ids = await verifyDb.Set<PlannerTask>().AsNoTracking()
            .Where(t => t.CalendarId == calendarId).Select(t => t.GoogleEventId).ToListAsync(CancellationToken);
        ids.Should().AllSatisfy(id => id.Should().BeNull("un-skipping the task later must insert a fresh event"));
    }

    /// <summary>
    /// P1-03: a block the portal reminds about is pushed with Google's popups off, or a phone with Google Calendar
    /// rings twice per block — and Google's alert ignores quiet hours, the away hold and snooze. A block without a
    /// portal reminder says <c>useDefault: true</c> explicitly, so an update restores the alert once the portal
    /// reminder is gone.
    /// </summary>
    [Fact(DisplayName = "P1-03: a block with a portal reminder is pushed with Google's own reminders off; one without keeps the defaults")]
    public async Task PortalReminder_TurnsOffGooglesOwnPopups()
    {
        var google = new FakeGoogleCalendarTransport();
        await using var factory = CreateFactoryWith(google);

        long calendarId;
        await using (var db = CreateDbContext())
        {
            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 7, 6), ct: TestContext.Current.CancellationToken);
            var remindedActivity = await PlanningTestSeedHelper.SeedActivityAsync(db, "google-reminded", ct: TestContext.Current.CancellationToken);
            var plainActivity = await PlanningTestSeedHelper.SeedActivityAsync(db, "google-plain", ct: TestContext.Current.CancellationToken);
            var remindedId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, remindedActivity, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: TestContext.Current.CancellationToken);
            await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, plainActivity, calendarId, new TimeOnly(11, 0), new TimeOnly(12, 0), ct: TestContext.Current.CancellationToken);

            db.Add(new Reminder
            {
                UserId = PlanningTestSeedHelper.TestUserId,
                Title = "google-reminded",
                RemindAt = new DateTime(2027, 7, 6, 9, 0, 0, DateTimeKind.Utc),
                PlannerTaskId = remindedId,
                IsAuto = true
            });
            await ConnectTestUserAsync(db);
        }

        var response = await factory.CreateClient().PostAsync($"api/calendar/{calendarId}/sync-to-google", null, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));

        var events = google.Bodies.Select(b => JsonDocument.Parse(b).RootElement).ToDictionary(e => e.GetProperty("summary").GetString()!);
        events.Should().HaveCount(2);

        var reminded = events["google-reminded"].GetProperty("reminders");
        reminded.GetProperty("useDefault").GetBoolean().Should().BeFalse();
        reminded.GetProperty("overrides").GetArrayLength().Should().Be(0);

        events["google-plain"].GetProperty("reminders").GetProperty("useDefault").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// The offset used to be <c>GetUtcOffset(DateTime.UtcNow)</c> — today's — applied to whatever date was synced, so a
    /// block on the far side of a DST switch landed an hour off. One winter and one summer day pin it whichever half
    /// of the year the test runs in.
    /// </summary>
    [Fact(DisplayName = "A block is pushed at its own day's UTC offset, not today's")]
    public async Task Block_IsPushedAtItsOwnDaysOffset()
    {
        var google = new FakeGoogleCalendarTransport();
        await using var factory = CreateFactoryWith(google);

        long winterId, summerId;
        await using (var db = CreateDbContext())
        {
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "google-sync-dst", ct: TestContext.Current.CancellationToken);
            winterId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 1, 15), ct: TestContext.Current.CancellationToken);
            summerId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 7, 15), ct: TestContext.Current.CancellationToken);
            await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, winterId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: TestContext.Current.CancellationToken);
            await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, summerId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: TestContext.Current.CancellationToken);
            await ConnectTestUserAsync(db, TimeZoneInfo.FindSystemTimeZoneById("Europe/Bratislava"));
        }

        await PostSyncAsync(factory, winterId);
        await PostSyncAsync(factory, summerId);

        google.Events.Values.Select(e => FakeGoogleCalendarTransport.Instant(e, "start")).Should().BeEquivalentTo(
        [
            new DateTimeOffset(2027, 1, 15, 8, 0, 0, TimeSpan.Zero), // 09:00 CET
            new DateTimeOffset(2027, 7, 15, 7, 0, 0, TimeSpan.Zero) // 09:00 CEST
        ]);
    }

    [Fact(DisplayName = "A block deleted in the app has its Google event removed; the user's own events are left alone")]
    public async Task DeletedBlock_HasItsEventRemoved()
    {
        var google = new FakeGoogleCalendarTransport();
        await using var factory = CreateFactoryWith(google);

        var day = new DateOnly(2027, 7, 7);
        long calendarId, keptId;
        await using (var db = CreateDbContext())
        {
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "google-sync-orphan", ct: TestContext.Current.CancellationToken);
            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, day, ct: TestContext.Current.CancellationToken);
            keptId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: TestContext.Current.CancellationToken);
            (await db.Set<PlannerTask>().SingleAsync(t => t.Id == keptId, CancellationToken)).GoogleEventId = "evt-kept";
            await ConnectTestUserAsync(db);
        }

        google.Seed("evt-kept", keptId, At(day, 9), At(day, 10));
        // An earlier sync's event whose block has since been deleted — no row left anywhere to say so.
        google.Seed("evt-deleted", 999_999, At(day, 11), At(day, 12));
        // Made by the user in Google: no marker, so it is not the sync's to touch.
        google.Seed("evt-users-own", plannerTaskId: null, At(day, 13), At(day, 14));

        var response = await PostSyncAsync(factory, calendarId);

        response.SyncedCount.Should().Be(1);
        response.RemovedCount.Should().Be(1);
        google.Writes.Where(r => r.Method == HttpMethod.Delete).Select(r => r.RequestUri!.Segments[^1])
            .Should().Equal("evt-deleted");
        google.Events.Keys.Should().BeEquivalentTo("evt-kept", "evt-users-own");
    }

    [Fact(DisplayName = "A block moved to another day has its old day's event removed, and is inserted again on its new day")]
    public async Task MovedBlock_IsRemovedFromItsOldDay_AndReinsertedOnItsNewOne()
    {
        var google = new FakeGoogleCalendarTransport();
        await using var factory = CreateFactoryWith(google);

        var oldDay = new DateOnly(2027, 7, 8);
        long oldDayId, newDayId, movedId;
        await using (var db = CreateDbContext())
        {
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "google-sync-moved", ct: TestContext.Current.CancellationToken);
            oldDayId = await PlanningTestSeedHelper.SeedCalendarAsync(db, oldDay, ct: TestContext.Current.CancellationToken);
            newDayId = await PlanningTestSeedHelper.SeedCalendarAsync(db, oldDay.AddDays(1), ct: TestContext.Current.CancellationToken);
            movedId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, newDayId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: TestContext.Current.CancellationToken);
            (await db.Set<PlannerTask>().SingleAsync(t => t.Id == movedId, CancellationToken)).GoogleEventId = "evt-moved";
            await ConnectTestUserAsync(db);
        }

        // Where the block was when it was last synced.
        google.Seed("evt-moved", movedId, At(oldDay, 9), At(oldDay, 10));

        (await PostSyncAsync(factory, oldDayId)).RemovedCount.Should().Be(1);

        await using (var db = CreateDbContext())
            (await db.Set<PlannerTask>().AsNoTracking().SingleAsync(t => t.Id == movedId, CancellationToken))
                .GoogleEventId.Should().BeNull("its event is gone, so its own day's sync must insert, not update");

        (await PostSyncAsync(factory, newDayId)).SyncedCount.Should().Be(1);

        google.Events.Should().ContainSingle().Which.Value["extendedProperties"]!["private"]!["plannerTaskId"]!
            .GetValue<string>().Should().Be(movedId.ToString());
    }

    [Fact(DisplayName = "An event Google no longer has is inserted afresh instead of failing the sync")]
    public async Task EventMissingOnGoogle_IsReinserted()
    {
        var google = new FakeGoogleCalendarTransport();
        await using var factory = CreateFactoryWith(google);

        long calendarId, taskId;
        await using (var db = CreateDbContext())
        {
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "google-sync-gone", ct: TestContext.Current.CancellationToken);
            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 7, 10), ct: TestContext.Current.CancellationToken);
            taskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: TestContext.Current.CancellationToken);
            (await db.Set<PlannerTask>().SingleAsync(t => t.Id == taskId, CancellationToken)).GoogleEventId = "evt-gone";
            await ConnectTestUserAsync(db);
        }

        // Deleted by the user in Google.
        google.MissingEventIds.Add("evt-gone");

        (await PostSyncAsync(factory, calendarId)).SyncedCount.Should().Be(1);

        google.Writes.Select(r => r.Method).Should().Equal(HttpMethod.Put, HttpMethod.Post);
        await using var verifyDb = CreateDbContext();
        var eventId = (await verifyDb.Set<PlannerTask>().AsNoTracking().SingleAsync(t => t.Id == taskId, CancellationToken)).GoogleEventId;
        eventId.Should().NotBeNull().And.NotBe("evt-gone");
        google.Events.Keys.Should().Equal(eventId!);
    }

    private static DateTimeOffset At(DateOnly day, int hour) => new(day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);

    private async Task<SyncResult> PostSyncAsync(ITestClientFactory factory, long calendarId)
    {
        var response = await factory.CreateClient().PostAsync($"api/calendar/{calendarId}/sync-to-google", null, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));
        return (await response.Content.ReadFromJsonAsync<SyncResult>(JsonOpts, CancellationToken))!;
    }

    private ITestClientFactory CreateFactoryWith(FakeGoogleCalendarTransport google)
    {
        // A fresh service per call, like the real one: the endpoint disposes it, so a shared instance would leave a
        // test's second sync talking to a disposed client.
        var googleServiceMock = new Mock<IGoogleCalendarService>(MockBehavior.Strict);
        googleServiceMock.Setup(s => s.GetCalendarService(It.IsAny<string>())).Returns((string _) =>
            new CalendarService(new Google.Apis.Services.BaseClientService.Initializer
            {
                ApplicationName = "AdhdTimeOrganizer-Test",
                HttpClientFactory = google,
                // Plain JSON on the wire, so the fake transport can read the bodies it records.
                GZipEnabled = false
            }));

        return CreateFactory(TestRoles.AdminAndUser, configureServices: services =>
        {
            services.RemoveAll<IGoogleCalendarService>();
            services.AddSingleton(googleServiceMock.Object);
        });
    }

    private async Task ConnectTestUserAsync(DbContext db, TimeZoneInfo? zone = null)
    {
        var user = await db.Set<User>().SingleAsync(u => u.Id == PlanningTestSeedHelper.TestUserId, CancellationToken);
        user.GoogleCalendarRefreshToken = "fake-refresh-token";
        if (zone is not null)
            user.Timezone = zone;
        await db.SaveChangesAsync(CancellationToken);
    }

    private record SyncResult(int SyncedCount, int RemovedCount);

    /// <summary>
    /// Stands in for Google's HTTP endpoint underneath a real <c>CalendarService</c>, so the endpoint's
    /// Insert/Update/Delete/List calls run for real and are recorded instead of leaving the process. It keeps what it
    /// was sent in <see cref="Events"/> and answers the day listing from it, honouring the two filters the sync relies
    /// on: the private-property marker and the time window.
    /// </summary>
    private sealed class FakeGoogleCalendarTransport : HttpClientFactory
    {
        private int _nextId;

        public List<HttpRequestMessage> Requests { get; } = [];
        public HashSet<string> MissingEventIds { get; } = [];

        /// <summary>The body of every insert and update, in order — read before it is answered, as the content is disposed after.</summary>
        public List<string> Bodies { get; } = [];

        /// <summary>What Google holds, by event id: every insert and update, plus whatever a test seeds.</summary>
        public Dictionary<string, JsonObject> Events { get; } = [];

        /// <summary>Every request but the day listing each sync ends with.</summary>
        public IEnumerable<HttpRequestMessage> Writes => Requests.Where(r => r.Method != HttpMethod.Get);

        /// <summary>
        /// An event already on Google: tagged as the sync's own for <paramref name="plannerTaskId"/>, as an earlier sync
        /// would have left it, or untagged — one the user made — when it is null.
        /// </summary>
        public void Seed(string eventId, long? plannerTaskId, DateTimeOffset start, DateTimeOffset end)
        {
            var googleEvent = new JsonObject
            {
                ["id"] = eventId,
                ["start"] = new JsonObject { ["dateTime"] = start.ToString("O", CultureInfo.InvariantCulture) },
                ["end"] = new JsonObject { ["dateTime"] = end.ToString("O", CultureInfo.InvariantCulture) }
            };
            if (plannerTaskId is { } taskId)
                googleEvent["extendedProperties"] = new JsonObject
                {
                    ["private"] = new JsonObject { ["adhdTimeOrganizer"] = "plannerTask", ["plannerTaskId"] = taskId.ToString() }
                };

            Events[eventId] = googleEvent;
        }

        public static DateTimeOffset Instant(JsonObject googleEvent, string edge) =>
            DateTimeOffset.Parse(googleEvent[edge]!["dateTime"]!.GetValue<string>(), CultureInfo.InvariantCulture);

        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => new Handler(this);

        /// <summary>The events carrying the queried marker and overlapping <c>[timeMin, timeMax)</c>.</summary>
        private JsonObject List(Uri uri)
        {
            var query = HttpUtility.ParseQueryString(uri.Query);
            var marker = query["privateExtendedProperty"]?.Split('=', 2);
            var timeMin = Parse(query["timeMin"]) ?? DateTimeOffset.MinValue;
            var timeMax = Parse(query["timeMax"]) ?? DateTimeOffset.MaxValue;

            var items = Events.Values
                .Where(e => marker is null || e["extendedProperties"]?["private"]?[marker[0]]?.GetValue<string>() == marker[1])
                .Where(e => Instant(e, "start") < timeMax && Instant(e, "end") > timeMin)
                .Select(e => e.DeepClone())
                .ToArray();

            return new JsonObject { ["items"] = new JsonArray(items) };
        }

        // A '+' offset decodes to a space in a query string.
        private static DateTimeOffset? Parse(string? value) =>
            value is null ? null : DateTimeOffset.Parse(value.Replace(' ', '+'), CultureInfo.InvariantCulture);

        private sealed class Handler(FakeGoogleCalendarTransport owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                owner.Requests.Add(request);
                var uri = request.RequestUri!;
                var eventId = uri.Segments[^1];

                if (request.Method == HttpMethod.Get)
                    return Respond(HttpStatusCode.OK, owner.List(uri).ToJsonString());

                if (request.Method != HttpMethod.Post && owner.MissingEventIds.Contains(eventId))
                    return Respond(HttpStatusCode.NotFound, """{"error":{"code":404,"message":"Not Found"}}""");

                if (request.Method == HttpMethod.Delete)
                {
                    owner.Events.Remove(eventId);
                    return Respond(HttpStatusCode.NoContent, string.Empty);
                }

                var body = request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
                owner.Bodies.Add(body);

                var stored = JsonNode.Parse(body)!.AsObject();
                var id = request.Method == HttpMethod.Post ? $"evt-{Interlocked.Increment(ref owner._nextId)}" : eventId;
                stored["id"] = id;
                owner.Events[id] = stored;
                return Respond(HttpStatusCode.OK, stored.ToJsonString());
            }

            private static Task<HttpResponseMessage> Respond(HttpStatusCode status, string json) =>
                Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
        }
    }
}
