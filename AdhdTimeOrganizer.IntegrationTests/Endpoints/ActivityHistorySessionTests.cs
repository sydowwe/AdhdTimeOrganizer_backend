using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.History.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// B3 — a timer session as one unit of history: <c>POST api/activity-history/session</c>, the session block on
/// every row read, the row delete's "last row takes the session with it", and the three counts that now count
/// sittings instead of rows.
/// </summary>
/// <remarks>
/// <para>
/// The counting tests are the ones that matter most, and they assert <b>numbers</b>: a count of rows is a
/// perfectly well-formed answer, so only an exact value distinguishes "one Pomodoro" from "three entries".
/// Each seeds the same shape — a three-phase Pomodoro (focus A, break B, focus A) plus one plain row of A — so
/// rows and sittings differ on every surface.
/// </para>
/// <para>
/// Atomicity is pinned twice, because it can fail at two layers: an ownership refusal on the second record (the
/// endpoint must check every record before writing any), and a unique-index collision on the second record (the
/// database refuses mid-graph, and the first record must not survive it).
/// </para>
/// </remarks>
[Collection("Postgres")]
public class ActivityHistorySessionTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string SessionRoute = "api/activity-history/session";

    private static long UserId => FakeLoggedUserService.TestUserId;

    /// <summary>Whole seconds, so the round trip through <c>timestamptz</c> cannot blur an equality.</summary>
    private static DateTime TenAm => DateTime.UtcNow.Date.AddHours(10);

    private static string Today => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
    private static object Midnight => new { hours = 0, minutes = 0 };

    // ---- 1. writing a session ----------------------------------------------------------------------

    [Fact(DisplayName = "A Pomodoro lands as one session with one row per phase, each at its real start")]
    public async Task Pomodoro_IsWrittenAsOneSession_WithRowsAtTheirRealStarts()
    {
        var (focusId, restId) = await SeedTwoActivitiesAsync();

        var response = await PostSessionAsync(PomodoroBody(focusId, restId, "StoppedEarly", planned: 8, completed: 2));

        var body = await ShouldBeCreatedAsync(response);
        var sessionId = body.GetProperty("sessionId").GetInt64();
        var recordIds = body.GetProperty("recordIds").EnumerateArray().Select(e => e.GetInt64()).ToList();
        recordIds.Should().HaveCount(3);

        await using var db = CreateDbContext();
        var rows = await db.Set<ActivityHistory>()
            .Where(h => h.SessionId == sessionId)
            .OrderBy(h => h.StartTimestamp)
            .ToListAsync(CancellationToken);

        rows.Select(r => r.Id).Should().Equal(recordIds, "recordIds come back in the order the records were sent");
        rows.Select(r => r.StartTimestamp).Should().Equal(TenAm, TenAm.AddMinutes(25), TenAm.AddMinutes(30));
        rows.Select(r => r.Phase).Should().Equal(TimerSessionPhase.Focus, TimerSessionPhase.ShortBreak, TimerSessionPhase.Focus);
        rows.Select(r => r.ActivityId).Should().Equal(focusId, restId, focusId);
        rows.Should().OnlyContain(r => r.EndTimestamp == r.StartTimestamp.AddSeconds(r.Length.TotalSeconds));

        var session = await db.Set<ActivityHistorySession>().SingleAsync(s => s.Id == sessionId, CancellationToken);
        session.Kind.Should().Be(TimerSessionKind.Pomodoro);
        session.Outcome.Should().Be(TimerSessionOutcome.StoppedEarly);
        session.PlannedFocusPeriods.Should().Be(8);
        session.CompletedFocusPeriods.Should().Be(2);
        session.PlannedLength.Should().BeNull("the body did not send one, and it is optional");
        session.UserId.Should().Be(UserId);
    }

    /// <summary>OP6: without the plan, a countdown stopped early cannot say by how much.</summary>
    [Fact(DisplayName = "A countdown stopped early keeps its planned length, and the row read returns it")]
    public async Task StoppedEarlyTimer_KeepsItsPlannedLength()
    {
        var (activityId, _) = await SeedTwoActivitiesAsync();

        var body = await ShouldBeCreatedAsync(await PostSessionAsync(
            SessionBody("Timer", "StoppedEarly", null, null, [Record(activityId, TenAm, 12, null)], plannedMinutes: 85)));
        var sessionId = body.GetProperty("sessionId").GetInt64();

        await using (var db = CreateDbContext())
        {
            var session = await db.Set<ActivityHistorySession>().SingleAsync(s => s.Id == sessionId, CancellationToken);
            session.PlannedLength.Should().Be(new IntTime(1, 25));
        }

        var row = (await PostOkAsync("api/activity-history/filter", new { date = Today, from = Midnight, to = Midnight }))
            .EnumerateArray().Should().ContainSingle().Subject;
        var planned = row.GetProperty("session").GetProperty("plannedLength");
        planned.GetProperty("hours").GetInt32().Should().Be(1);
        planned.GetProperty("minutes").GetInt32().Should().Be(25);
        row.GetProperty("length").GetProperty("minutes").GetInt32().Should().Be(12, "the row is what ran, not the plan");
    }

    [Fact(DisplayName = "A countdown is a one-row Timer session with no phase and no counts")]
    public async Task Timer_IsWrittenAsOneRowSession()
    {
        var (activityId, _) = await SeedTwoActivitiesAsync();

        var response = await PostSessionAsync(new
        {
            kind = "Timer",
            outcome = "Completed",
            plannedFocusPeriods = (int?)null,
            completedFocusPeriods = (int?)null,
            records = new[] { Record(activityId, TenAm, 20, phase: null) }
        });

        var body = await ShouldBeCreatedAsync(response);
        var rowId = body.GetProperty("recordIds").EnumerateArray().Single().GetInt64();

        await using var db = CreateDbContext();
        var row = await db.Set<ActivityHistory>().Include(h => h.Session).SingleAsync(h => h.Id == rowId, CancellationToken);
        row.Phase.Should().BeNull();
        row.Session!.Kind.Should().Be(TimerSessionKind.Timer);
        row.Session.Outcome.Should().Be(TimerSessionOutcome.Completed);
        row.Session.PlannedFocusPeriods.Should().BeNull();
    }

    /// <summary>Rule 6: rounding a phase up can push its end up to 59 s past the next phase's start.</summary>
    [Fact(DisplayName = "Records overlapping by under a minute are accepted")]
    public async Task SubMinuteOverlap_IsAccepted()
    {
        var (focusId, restId) = await SeedTwoActivitiesAsync();

        var response = await PostSessionAsync(new
        {
            kind = "Pomodoro",
            outcome = "Completed",
            plannedFocusPeriods = 1,
            completedFocusPeriods = 1,
            records = new[]
            {
                // 12:40 rounded up to 13 minutes, so this row ends 20 s after the break starts.
                Record(focusId, TenAm, 13, "Focus"),
                Record(restId, TenAm.AddMinutes(12).AddSeconds(40), 5, "ShortBreak")
            }
        });

        await ShouldBeCreatedAsync(response);
    }

    // ---- 2. malformed sessions: 400, and nothing written --------------------------------------------

    public static TheoryData<string> MalformedCases =>
    [
        "no records",
        "timer with two records",
        "timer ran out while away",
        "completed more than planned",
        "pomodoro without counts",
        "timer with counts",
        "zero-length record",
        "records out of order",
        "pomodoro record without phase",
        "timer record with phase",
        "start without offset",
        "zero planned length",
        "linked to both item kinds"
    ];

    [Theory(DisplayName = "A malformed session is a 400 and writes nothing")]
    [MemberData(nameof(MalformedCases))]
    public async Task MalformedSession_IsRejected_AndWritesNothing(string malformed)
    {
        var (focusId, restId) = await SeedTwoActivitiesAsync();

        var body = malformed switch
        {
            "no records" => SessionBody("Pomodoro", "Completed", 1, 1, []),
            "timer with two records" => SessionBody("Timer", "Completed", null, null,
                [Record(focusId, TenAm, 10, null), Record(focusId, TenAm.AddMinutes(10), 10, null)]),
            "timer ran out while away" => SessionBody("Timer", "RanOutWhileAway", null, null, [Record(focusId, TenAm, 10, null)]),
            "completed more than planned" => SessionBody("Pomodoro", "Completed", 1, 2, [Record(focusId, TenAm, 25, "Focus")]),
            "pomodoro without counts" => SessionBody("Pomodoro", "Completed", null, null, [Record(focusId, TenAm, 25, "Focus")]),
            "timer with counts" => SessionBody("Timer", "Completed", 1, 1, [Record(focusId, TenAm, 10, null)]),
            "zero-length record" => SessionBody("Pomodoro", "StoppedEarly", 2, 1,
                [Record(focusId, TenAm, 25, "Focus"), Record(restId, TenAm.AddMinutes(25), 0, "ShortBreak")]),
            "records out of order" => SessionBody("Pomodoro", "Completed", 1, 1,
                [Record(restId, TenAm.AddMinutes(25), 5, "ShortBreak"), Record(focusId, TenAm, 25, "Focus")]),
            "pomodoro record without phase" => SessionBody("Pomodoro", "Completed", 1, 1, [Record(focusId, TenAm, 25, null)]),
            "timer record with phase" => SessionBody("Timer", "Completed", null, null, [Record(focusId, TenAm, 10, "Focus")]),
            "start without offset" => SessionBody("Timer", "Completed", null, null,
                [new { activityId = focusId, startTimestamp = "2026-09-29T10:00:00", length = new { hours = 0, minutes = 10 }, phase = (string?)null }]),
            "zero planned length" => SessionBody("Timer", "StoppedEarly", null, null, [Record(focusId, TenAm, 10, null)], plannedMinutes: 0),
            "linked to both item kinds" => new
            {
                kind = "Timer", outcome = "Completed", todoListItemId = 1L, routineTodoListId = 1L,
                records = new[] { Record(focusId, TenAm, 10, null) }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(malformed))
        };

        var response = await PostSessionAsync(body);

        var text = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"{malformed}: {text}");
        (await CountRowsAsync(focusId, restId)).Should().Be(0, "a rejected session must not have written anything");
        (await CountSessionsAsync()).Should().Be(0);
    }

    // ---- 3. all-or-nothing --------------------------------------------------------------------------

    [Fact(DisplayName = "An unowned activity on the second record is a 404 on that record, and nothing is written")]
    public async Task UnownedActivityOnALaterRecord_Is404_AndWritesNothing()
    {
        var (focusId, _) = await SeedTwoActivitiesAsync();
        var strangerId = await ActivityProfileTestSeedHelper.CreateSecondUserAsync(Fixture,$"session-stranger-{Guid.NewGuid():N}@test.com");
        long foreignActivityId;
        await using (var db = CreateDbContext())
            foreignActivityId = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, strangerId, "Someone else's");

        var response = await PostSessionAsync(SessionBody("Pomodoro", "Completed", 1, 1,
            [Record(focusId, TenAm, 25, "Focus"), Record(foreignActivityId, TenAm.AddMinutes(25), 5, "ShortBreak")]));

        var text = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, text);
        JsonDocument.Parse(text).RootElement.GetProperty("errors").EnumerateObject()
            .Select(e => e.Name)
            .Should().Contain(n => n.Equals("records[1].activityId", StringComparison.OrdinalIgnoreCase),
                "the refusal is keyed on the offending record's field, as the single create keys it on activityId");

        (await CountRowsAsync(focusId)).Should().Be(0, "the first record's activity was fine — it still must not land alone");
        (await CountSessionsAsync()).Should().Be(0);
    }

    /// <summary>
    /// The item link has no navigation, so the endpoint is all that stops a session crediting its time to an item
    /// the caller does not own. "Missing" and "foreign" answer the same.
    /// </summary>
    [Fact(DisplayName = "An unowned to-do item link is a 404 on that field, and nothing is written")]
    public async Task UnownedTodoItemLink_Is404_AndWritesNothing()
    {
        var (focusId, _) = await SeedTwoActivitiesAsync();

        var response = await PostSessionAsync(new
        {
            kind = "Timer",
            outcome = "Completed",
            todoListItemId = long.MaxValue,
            records = new[] { Record(focusId, TenAm, 10, null) }
        });

        var text = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, text);
        JsonDocument.Parse(text).RootElement.GetProperty("errors").EnumerateObject()
            .Select(e => e.Name)
            .Should().Contain(n => n.Equals("todoListItemId", StringComparison.OrdinalIgnoreCase));
        (await CountRowsAsync(focusId)).Should().Be(0);
        (await CountSessionsAsync()).Should().Be(0);
    }

    /// <summary>
    /// The database refusing mid-graph. The second record collides with an existing row on
    /// <c>(UserId, ActivityId, StartTimestamp)</c> — a double-submitted save is the realistic producer — so the
    /// insert of the first record has already been issued when the second fails. One transaction is what keeps
    /// it from surviving.
    /// </summary>
    [Fact(DisplayName = "A database refusal on the second record rolls back the first")]
    public async Task DatabaseRefusalMidSession_RollsBackEverything()
    {
        var (focusId, restId) = await SeedTwoActivitiesAsync();
        await SeedPlainRowAsync(restId, TenAm.AddMinutes(25), 5);

        var response = await PostSessionAsync(SessionBody("Pomodoro", "Completed", 1, 1,
            [Record(focusId, TenAm, 25, "Focus"), Record(restId, TenAm.AddMinutes(25), 5, "ShortBreak")]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync(CancellationToken));
        (await CountRowsAsync(focusId)).Should().Be(0, "the focus record must not survive its session's failure");
        (await CountRowsAsync(restId)).Should().Be(1, "only the pre-existing row");
        (await CountSessionsAsync()).Should().Be(0);
    }

    // ---- 4. reading, editing, deleting --------------------------------------------------------------

    [Fact(DisplayName = "The filter read carries the session and phase on session rows, and null on plain rows")]
    public async Task FilterRead_CarriesSessionAndPhase()
    {
        var (focusId, restId) = await SeedPomodoroAndPlainRowAsync();

        var rows = (await PostOkAsync("api/activity-history/filter", new { date = Today, from = Midnight, to = Midnight }))
            .EnumerateArray().ToList();
        rows.Should().HaveCount(4);

        var sessionRows = rows.Where(r => r.GetProperty("session").ValueKind != JsonValueKind.Null).ToList();
        sessionRows.Should().HaveCount(3);
        sessionRows.Select(r => r.GetProperty("session").GetProperty("id").GetInt64()).Distinct().Should().ContainSingle(
            "the same session object repeats on each of its rows, so the timeline can group without a second request");

        var session = sessionRows[0].GetProperty("session");
        session.GetProperty("kind").GetString().Should().Be("Pomodoro");
        session.GetProperty("outcome").GetString().Should().Be("StoppedEarly");
        session.GetProperty("plannedFocusPeriods").GetInt32().Should().Be(4);
        session.GetProperty("completedFocusPeriods").GetInt32().Should().Be(2);

        sessionRows.Select(r => r.GetProperty("phase").GetString())
            .Should().BeEquivalentTo(["Focus", "ShortBreak", "Focus"]);

        var plain = rows.Single(r => r.GetProperty("session").ValueKind == JsonValueKind.Null);
        plain.GetProperty("phase").ValueKind.Should().Be(JsonValueKind.Null);
        plain.GetProperty("activity").GetProperty("id").GetInt64().Should().Be(focusId);
        _ = restId;
    }

    /// <summary>Rule 7: the edit is row-level and does not detach the row or touch the session's outcome.</summary>
    [Fact(DisplayName = "Editing a session row keeps it in its session")]
    public async Task EditingARow_KeepsItInItsSession()
    {
        var (focusId, _) = await SeedTwoActivitiesAsync();
        var body = await ShouldBeCreatedAsync(await PostSessionAsync(
            SessionBody("Pomodoro", "Completed", 1, 1, [Record(focusId, TenAm, 25, "Focus")])));
        var sessionId = body.GetProperty("sessionId").GetInt64();
        var rowId = body.GetProperty("recordIds")[0].GetInt64();

        var put = await CreateUserRoleClient().PutAsJsonAsync($"api/activity-history/{rowId}", new
        {
            activityId = focusId,
            startTimestamp = TenAm,
            length = new { hours = 0, minutes = 20 }
        }, JsonOpts, CancellationToken);
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync(CancellationToken));

        await using var db = CreateDbContext();
        var row = await db.Set<ActivityHistory>().Include(h => h.Session).SingleAsync(h => h.Id == rowId, CancellationToken);
        row.SessionId.Should().Be(sessionId);
        row.Phase.Should().Be(TimerSessionPhase.Focus);
        row.Session!.Outcome.Should().Be(TimerSessionOutcome.Completed);
    }

    /// <summary>Rule 8.</summary>
    [Fact(DisplayName = "Deleting a phase keeps the session; deleting its last row removes it")]
    public async Task DeletingRows_RemovesTheSessionOnlyWithItsLastRow()
    {
        var (focusId, restId) = await SeedTwoActivitiesAsync();
        var body = await ShouldBeCreatedAsync(await PostSessionAsync(SessionBody("Pomodoro", "StoppedEarly", 2, 1,
            [Record(focusId, TenAm, 25, "Focus"), Record(restId, TenAm.AddMinutes(25), 5, "ShortBreak")])));
        var sessionId = body.GetProperty("sessionId").GetInt64();
        var rowIds = body.GetProperty("recordIds").EnumerateArray().Select(e => e.GetInt64()).ToList();

        await DeleteRowAsync(rowIds[0]);
        (await SessionExistsAsync(sessionId)).Should().BeTrue("one phase is gone; the session still has the other");

        await DeleteRowAsync(rowIds[1]);
        (await SessionExistsAsync(sessionId)).Should().BeFalse("a session with no rows must not outlive them");
    }

    // ---- 5. counting sittings, not rows ------------------------------------------------------------

    [Fact(DisplayName = "aggregate-by-activity counts sessions: one Pomodoro is one entry, not two")]
    public async Task AggregateByActivity_CountsSittings()
    {
        var (focusId, restId) = await SeedPomodoroAndPlainRowAsync();

        var entries = (await PostOkAsync("api/activity-history/aggregate-by-activity", new { activityIds = new[] { focusId, restId } }))
            .EnumerateArray()
            .ToDictionary(e => e.GetProperty("activityId").GetInt64(), e => e);

        entries[focusId].GetProperty("entryCount").GetInt32().Should().Be(2,
            "two focus rows of one Pomodoro plus one plain row are two sittings — counted as rows, 'you usually "
            + "take' would divide 70 minutes by three");
        entries[focusId].GetProperty("totalSeconds").GetInt64().Should().Be((25 + 15 + 30) * 60, "sums are unchanged");
        entries[restId].GetProperty("entryCount").GetInt32().Should().Be(1);
    }

    [Fact(DisplayName = "The pie counts sessions per group, and each session once in the total")]
    public async Task PieChart_CountsSittings()
    {
        var (focusId, restId) = await SeedPomodoroAndPlainRowAsync();

        var pie = await PostOkAsync("api/activity-history/dashboard/detail/pie-chart",
            new { date = Today, from = Midnight, to = Midnight, groupBy = "Activity", maxItems = 20 });

        var items = pie.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("groupId").GetInt64(), i => i);
        items[focusId].GetProperty("entries").GetInt32().Should().Be(2);
        items[restId].GetProperty("entries").GetInt32().Should().Be(1,
            "the session touches the rest group too, so it counts once there as well");
        pie.GetProperty("totals").GetProperty("totalEntries").GetInt32().Should().Be(2,
            "the total counts each sitting once: one Pomodoro and one plain row, not four rows");
    }

    [Fact(DisplayName = "The calendar's sessionCount counts sessions")]
    public async Task Calendar_SessionCount_CountsSittings()
    {
        await using (var db = CreateDbContext())
            await PlanningTestSeedHelper.SeedCalendarAsync(db, DateOnly.FromDateTime(DateTime.UtcNow), UserId, CancellationToken);
        await SeedPomodoroAndPlainRowAsync();

        var day = (await PostOkAsync("api/activity-history/dashboard/calendar", new { startDate = Today, endDate = Today }))
            .EnumerateArray().Should().ContainSingle().Subject;

        day.GetProperty("sessionCount").GetInt32().Should().Be(2, "one Pomodoro and one plain row");
    }

    // ---- 6. the heartbeat probe ---------------------------------------------------------------------

    /// <summary>
    /// A tracked window starting exactly where a session's last row ends must not grow that row: the row is
    /// what the timer measured, and its session's outcome describes it.
    /// </summary>
    [Fact(DisplayName = "The heartbeat's contiguous-window extension skips session rows")]
    public async Task Attribution_DoesNotExtendASessionRow()
    {
        var (focusId, _) = await SeedTwoActivitiesAsync();
        var body = await ShouldBeCreatedAsync(await PostSessionAsync(
            SessionBody("Timer", "Completed", null, null, [Record(focusId, TenAm, 10, null)])));
        var rowId = body.GetProperty("recordIds")[0].GetInt64();

        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        var sink = scope.ServiceProvider.GetRequiredService<IActivityTimeAttributionSink>();

        await using (var db = CreateDbContext())
        {
            await sink.AttributeAsync(db, UserId, focusId, TenAm.AddMinutes(10), 120, CancellationToken);
            await db.SaveChangesAsync(CancellationToken);
        }

        await using var read = CreateDbContext();
        var rows = await read.Set<ActivityHistory>().Where(h => h.ActivityId == focusId)
            .OrderBy(h => h.StartTimestamp).ToListAsync(CancellationToken);

        rows.Should().HaveCount(2, "the tracked stretch becomes its own row after the session");
        rows[0].Id.Should().Be(rowId);
        rows[0].Length.TotalSeconds.Should().Be(600, "the session's row keeps what the timer measured");
        rows[1].SessionId.Should().BeNull();
    }

    // ---- helpers -----------------------------------------------------------------------------------

    /// <summary>
    /// A three-phase Pomodoro stopped after 2 of 4 — focus A 25 min, break B 5 min, focus A 15 min — plus one
    /// plain 30-minute row of A later the same day. Rows: A×3, B×1. Sittings: A×2, B×1, two in total.
    /// </summary>
    private async Task<(long FocusId, long RestId)> SeedPomodoroAndPlainRowAsync()
    {
        var (focusId, restId) = await SeedTwoActivitiesAsync();

        await ShouldBeCreatedAsync(await PostSessionAsync(SessionBody("Pomodoro", "StoppedEarly", 4, 2,
        [
            Record(focusId, TenAm, 25, "Focus"),
            Record(restId, TenAm.AddMinutes(25), 5, "ShortBreak"),
            Record(focusId, TenAm.AddMinutes(30), 15, "Focus")
        ])));

        await SeedPlainRowAsync(focusId, TenAm.AddHours(3), 30);

        return (focusId, restId);
    }

    private static object PomodoroBody(long focusId, long restId, string outcome, int planned, int completed) =>
        SessionBody("Pomodoro", outcome, planned, completed,
        [
            Record(focusId, TenAm, 25, "Focus"),
            Record(restId, TenAm.AddMinutes(25), 5, "ShortBreak"),
            Record(focusId, TenAm.AddMinutes(30), 12, "Focus")
        ]);

    private static object SessionBody(string kind, string outcome, int? planned, int? completed, object[] records,
        int? plannedMinutes = null) => new
    {
        kind,
        outcome,
        plannedFocusPeriods = planned,
        completedFocusPeriods = completed,
        plannedLength = plannedMinutes is { } m ? new { hours = m / 60, minutes = m % 60 } : null,
        records
    };

    private static object Record(long activityId, DateTime start, int minutes, string? phase) => new
    {
        activityId,
        startTimestamp = start,
        length = new { hours = 0, minutes },
        phase
    };

    private Task<HttpResponseMessage> PostSessionAsync(object body) =>
        CreateUserRoleClient().PostAsJsonAsync(SessionRoute, body, JsonOpts, CancellationToken);

    private async Task<JsonElement> ShouldBeCreatedAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private async Task<JsonElement> PostOkAsync(string route, object body)
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(route, body, JsonOpts, CancellationToken);
        var text = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private async Task DeleteRowAsync(long rowId)
    {
        var response = await CreateUserRoleClient().DeleteAsync($"api/activity-history/{rowId}", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private async Task<(long, long)> SeedTwoActivitiesAsync()
    {
        await using var db = CreateDbContext();
        var focusId = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, UserId, $"Deep work {Guid.NewGuid():N}");
        var restId = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, UserId, $"Rest {Guid.NewGuid():N}");
        return (focusId, restId);
    }

    private async Task SeedPlainRowAsync(long activityId, DateTime start, int minutes)
    {
        await using var db = CreateDbContext();
        db.Set<ActivityHistory>().Add(new ActivityHistory
        {
            UserId = UserId,
            ActivityId = activityId,
            StartTimestamp = start,
            Length = new IntTime(0, minutes),
            EndTimestamp = start.AddMinutes(minutes)
        });
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task<int> CountRowsAsync(params long[] activityIds)
    {
        await using var db = CreateDbContext();
        return await db.Set<ActivityHistory>().IgnoreQueryFilters()
            .CountAsync(h => activityIds.Contains(h.ActivityId), CancellationToken);
    }

    private async Task<int> CountSessionsAsync()
    {
        await using var db = CreateDbContext();
        return await db.Set<ActivityHistorySession>().IgnoreQueryFilters().CountAsync(CancellationToken);
    }

    private async Task<bool> SessionExistsAsync(long sessionId)
    {
        await using var db = CreateDbContext();
        return await db.Set<ActivityHistorySession>().IgnoreQueryFilters().AnyAsync(s => s.Id == sessionId, CancellationToken);
    }
}
