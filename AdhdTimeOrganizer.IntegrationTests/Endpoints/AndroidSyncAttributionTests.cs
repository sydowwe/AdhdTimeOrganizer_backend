using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.android;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// FO9 (F2-01) — phone time counts. <c>AndroidSyncEndpoint</c> now resolves each newly stored session through
/// the user's android mapping rules, attributes the matched ones to History through
/// <c>IActivityTimeAttributionSink.AttributeSpanAsync</c>, and publishes <c>ActivityTimeRecordedEvent</c> for the
/// user's today. Before this an android rule was saved, listed and applied to nothing.
/// </summary>
/// <remarks>
/// <para>
/// Every case asserts on rows, because both halves fail silently: an unattributed session still answers 200 with
/// <c>accepted: 1</c>, and an unpublished event leaves the block <i>Not started</i>. The sink's overlap arithmetic
/// is pinned at service level in <see cref="ActivityTimeAttributionSinkTests"/>; this class proves the route reaches
/// it, and the day rule.
/// </para>
/// <para>
/// The sync rejects a session more than five minutes in the future, so a case that needs "earlier today" moves
/// the fixture user onto a zone where it is daytime right now — the same run-time zone search as
/// <c>PlanningTestSeedHelper.UseZoneOffsetFromUtcTodayAsync</c>, and safe for the same reason (Respawn).
/// </para>
/// </remarks>
[Collection("Postgres")]
public class AndroidSyncAttributionTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private const string Route = "/api/activity-tracking/android/sync";

    private static readonly string[] TrackingRoles = ["User", "ActivityTracking"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private ITestClientFactory? _trackingFactory;

    [Fact(DisplayName = "A mapped session lands in History and completes today's block for its activity")]
    public async Task MappedSession_IsAttributed_AndCompletesTodaysBlock()
    {
        var (zone, localStart) = await UseDaytimeZoneAsync();
        var activityId = await SeedActivityWithRuleAsync("com.amazon.kindle");
        var taskId = await SeedBlockAsync(activityId, DateOnly.FromDateTime(localStart),
            TimeOnly.FromDateTime(localStart), TimeOnly.FromDateTime(localStart.AddMinutes(30)));
        var startUtc = WallClockZone.ToUtc(localStart, zone);

        await SyncAsync(Session("com.amazon.kindle", startUtc, 30));

        var row = (await HistoryAsync(activityId)).Should().ContainSingle().Subject;
        row.StartTimestamp.Should().Be(startUtc);
        row.Length.TotalSeconds.Should().Be(30 * 60);

        var task = await LoadTaskAsync(taskId);
        task.Status.Should().Be(PlannerTaskStatus.Completed,
            "the sync must publish ActivityTimeRecordedEvent for today -- NotStarted means it never did");
        task.ActualEndTime.Should().Be(TimeOnly.FromDateTime(localStart.AddMinutes(30)), "the event's window is on the user's clock");
    }

    /// <summary>
    /// A phone that never saw the response re-posts its buffer. The session is skipped by the
    /// <c>(device, package, start)</c> dedup before the rules run, so its time is not attributed a second time.
    /// </summary>
    [Fact(DisplayName = "A re-sent session is not attributed twice")]
    public async Task ResentSession_IsAttributedOnce()
    {
        var start = TruncateToMinute(DateTime.UtcNow.AddHours(-3));
        var activityId = await SeedActivityWithRuleAsync("com.amazon.kindle");

        await SyncAsync(Session("com.amazon.kindle", start, 20));
        await SyncAsync(Session("com.amazon.kindle", start, 20));

        (await HistoryAsync(activityId)).Should().ContainSingle()
            .Which.Length.TotalSeconds.Should().Be(20 * 60);
    }

    /// <summary>
    /// The double-count rule over the route: twenty minutes of reading already on the ledger (a timer, or the
    /// desktop heartbeat) and a thirty-minute phone session starting ten minutes in add up to forty, not fifty.
    /// </summary>
    [Fact(DisplayName = "Phone time the same activity already has is not counted again")]
    public async Task OverlapWithTheSameActivity_CountsOnlyTheUncoveredPart()
    {
        var start = TruncateToMinute(DateTime.UtcNow.AddHours(-3));
        var activityId = await SeedActivityWithRuleAsync("com.amazon.kindle");
        await using (var db = CreateDbContext())
        {
            db.Set<ActivityHistory>().Add(new ActivityHistory
            {
                UserId = UserId,
                ActivityId = activityId,
                StartTimestamp = start,
                EndTimestamp = start.AddMinutes(20),
                Length = new IntTime(20 * 60)
            });
            await db.SaveChangesAsync(CancellationToken);
        }

        await SyncAsync(Session("com.amazon.kindle", start.AddMinutes(10), 30));

        (await HistoryAsync(activityId)).Sum(h => h.Length.TotalSeconds).Should().Be(40 * 60);
    }

    /// <summary>
    /// No rule, or an ignore rule, attributes nothing — but the session is still stored, because the android
    /// ledger never filtered on rules and its dashboards read it whole.
    /// </summary>
    [Fact(DisplayName = "An unmapped or ignored package is stored but attributes nothing")]
    public async Task UnmappedAndIgnoredSessions_AreStored_ButAttributeNothing()
    {
        var start = TruncateToMinute(DateTime.UtcNow.AddHours(-3));
        await using (var db = CreateDbContext())
        {
            db.Set<TrackerAndroidMappingByPattern>().Add(new TrackerAndroidMappingByPattern
            {
                UserId = UserId,
                PackageName = "com.ignored",
                PackageNameMatchType = PatternMatchType.Exact,
                IsActive = true,
                IsIgnored = true
            });
            await db.SaveChangesAsync(CancellationToken);
        }

        var response = await SyncAsync(Session("com.ignored", start, 20), Session("com.unmapped", start, 20));

        (await response.Content.ReadFromJsonAsync<JsonElement>(Json, CancellationToken))
            .GetProperty("accepted").GetInt32().Should().Be(2);
        await using var read = CreateDbContext();
        (await read.Set<AndroidSessionData>().IgnoreQueryFilters().CountAsync(s => s.UserId == UserId, CancellationToken))
            .Should().Be(2);
        (await read.Set<ActivityHistory>().IgnoreQueryFilters().CountAsync(h => h.UserId == UserId, CancellationToken))
            .Should().Be(0);
    }

    /// <summary>
    /// A sync is a backlog upload. Yesterday's reading is attributed — History and the dashboards should know —
    /// but it drives no completion: the handler's to-do fallback has no day, so publishing it would tick off an
    /// item today with yesterday's time. Both the block on that day and the open item stay as they were.
    /// </summary>
    [Fact(DisplayName = "A session from another day is attributed but ticks nothing off")]
    public async Task PastDaySession_IsAttributed_ButDrivesNoCompletion()
    {
        var (zone, localNow) = await UseDaytimeZoneAsync();
        var yesterday = DateOnly.FromDateTime(localNow).AddDays(-1);
        var localStart = yesterday.ToDateTime(new TimeOnly(10, 0));
        var activityId = await SeedActivityWithRuleAsync("com.amazon.kindle");
        var taskId = await SeedBlockAsync(activityId, yesterday, new TimeOnly(10, 0), new TimeOnly(10, 30));
        long itemId;
        await using (var db = CreateDbContext())
        {
            var priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 1, ct: CancellationToken);
            var listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, ct: CancellationToken);
            itemId = await TodoListTestSeedHelper.SeedTodoListItemAsync(db, activityId, priorityId, listId,
                ct: CancellationToken, suggestedTime: new IntTime(0, 30));
        }

        await SyncAsync(Session("com.amazon.kindle", WallClockZone.ToUtc(localStart, zone), 30));

        (await HistoryAsync(activityId)).Should().ContainSingle().Which.Length.TotalSeconds.Should().Be(30 * 60);
        (await LoadTaskAsync(taskId)).Status.Should().Be(PlannerTaskStatus.NotStarted);
        await using var read = CreateDbContext();
        (await read.Set<TodoListItem>().IgnoreQueryFilters().SingleAsync(i => i.Id == itemId, CancellationToken))
            .IsDone.Should().BeFalse("yesterday's time must not tick off an item today");
    }

    // ---- helpers ---------------------------------------------------------------------------------

    /// <summary>
    /// Moves the fixture user onto a system zone where it is between 04:00 and 22:00 right now, and returns it
    /// with a local start an hour ago, minute-aligned — "earlier today" for as long as a test runs.
    /// </summary>
    private async Task<(TimeZoneInfo Zone, DateTime LocalStart)> UseDaytimeZoneAsync()
    {
        var utcNow = DateTime.UtcNow;
        var zone = TimeZoneInfo.GetSystemTimeZones().First(z =>
        {
            var hour = TimeZoneInfo.ConvertTimeFromUtc(utcNow, z).Hour;
            return hour is >= 4 and < 22;
        });

        await using (var db = CreateDbContext())
        {
            var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == UserId, CancellationToken);
            user.Timezone = zone;
            await db.SaveChangesAsync(CancellationToken);
        }

        var localStart = TruncateToMinute(TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone).AddHours(-1));
        return (zone, DateTime.SpecifyKind(localStart, DateTimeKind.Unspecified));
    }

    private async Task<long> SeedActivityWithRuleAsync(string packageName)
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"android-{Guid.NewGuid():N}", ct: CancellationToken);
        db.Set<TrackerAndroidMappingByPattern>().Add(new TrackerAndroidMappingByPattern
        {
            UserId = UserId,
            PackageName = packageName,
            PackageNameMatchType = PatternMatchType.Exact,
            IsActive = true,
            ActivityId = activityId
        });
        await db.SaveChangesAsync(CancellationToken);
        return activityId;
    }

    private async Task<long> SeedBlockAsync(long activityId, DateOnly day, TimeOnly start, TimeOnly end)
    {
        await using var db = CreateDbContext();
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, day, UserId, CancellationToken);
        return await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, start, end, ct: CancellationToken);
    }

    private async Task<PlannerTask> LoadTaskAsync(long taskId)
    {
        await using var db = CreateDbContext();
        return await db.Set<PlannerTask>().IgnoreQueryFilters().SingleAsync(t => t.Id == taskId, CancellationToken);
    }

    private async Task<List<ActivityHistory>> HistoryAsync(long activityId)
    {
        await using var db = CreateDbContext();
        return await db.Set<ActivityHistory>().IgnoreQueryFilters()
            .Where(h => h.UserId == UserId && h.ActivityId == activityId)
            .OrderBy(h => h.StartTimestamp)
            .ToListAsync(CancellationToken);
    }

    private static DateTime TruncateToMinute(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerMinute, value.Kind);

    private static AndroidSessionItemDto Session(string packageName, DateTime startUtc, int minutes) =>
        new()
        {
            PackageName = packageName,
            AppLabel = packageName,
            SessionStartUtc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc).ToString("O"),
            SessionEndUtc = DateTime.SpecifyKind(startUtc.AddMinutes(minutes), DateTimeKind.Utc).ToString("O"),
            DurationSeconds = minutes * 60
        };

    private async Task<HttpResponseMessage> SyncAsync(params AndroidSessionItemDto[] sessions)
    {
        var response = await (_trackingFactory ??= CreateFactory(TrackingRoles, UserId)).CreateClient()
            .PostAsJsonAsync(Route, new AndroidSyncRequest
            {
                DeviceId = "pixel-8",
                SyncedUpToUtc = DateTime.UtcNow,
                Sessions = [.. sessions]
            }, Json, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));
        return response;
    }

    public override async ValueTask DisposeAsync()
    {
        if (_trackingFactory is not null)
            await _trackingFactory.DisposeAsync();

        await base.DisposeAsync();
    }
}
