using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// Planner lanes: at most two blocks of one kind at any minute (<see cref="PlannerTaskLaneRule"/>), refused on
/// create, the full update and the span patch with a 409 <c>Conflict</c>. Two side by side are allowed — that is
/// the point of the lanes — and a day that already holds three still lets the user edit a block in place.
/// </summary>
[Collection("Postgres")]
public class PlannerTaskLaneRuleTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Theory(DisplayName = "Lane limit: a third block at the same minute exceeds it, two do not")]
    [InlineData(9, 30, 9, 45, true)] // inside both
    [InlineData(8, 0, 9, 0, false)] // touches the first only at its start
    [InlineData(10, 0, 11, 0, false)] // touches both at their end
    [InlineData(9, 50, 10, 30, true)] // overlaps both for ten minutes
    public void ExceedsLaneLimit_CountsOverlapAtAMinute(int sh, int sm, int eh, int em, bool expected)
    {
        (TimeOnly, TimeOnly)[] day = [(new TimeOnly(9, 0), new TimeOnly(10, 0)), (new TimeOnly(9, 15), new TimeOnly(10, 0))];
        PlannerTaskLaneRule.ExceedsLaneLimit(new TimeOnly(sh, sm), new TimeOnly(eh, em), day).Should().Be(expected);
    }

    [Fact(DisplayName = "Lane limit: a block between two that never meet is allowed")]
    public void ExceedsLaneLimit_TwoOthersAtDifferentMinutes_IsAllowed()
    {
        (TimeOnly, TimeOnly)[] day = [(new TimeOnly(9, 0), new TimeOnly(10, 0)), (new TimeOnly(10, 0), new TimeOnly(11, 0))];
        PlannerTaskLaneRule.ExceedsLaneLimit(new TimeOnly(9, 30), new TimeOnly(10, 30), day).Should().BeFalse();
    }

    [Fact(DisplayName = "Create: a second overlapping block is accepted, a third is a 409 Conflict")]
    public async Task Create_RefusesAThirdOverlappingBlock()
    {
        var (activityId, calendarId) = await SeedDayAsync("lanes-create");

        (await CreateAsync(activityId, calendarId, (9, 0), (10, 0))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await CreateAsync(activityId, calendarId, (9, 30), (10, 30))).StatusCode.Should().Be(HttpStatusCode.Created,
            "two blocks side by side are allowed");

        var third = await CreateAsync(activityId, calendarId, (9, 45), (10, 15));
        third.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await third.Content.ReadAsStringAsync(CancellationToken)).Should().Contain("Conflict").And.Contain("startTime");

        await using var db = CreateDbContext();
        (await db.Set<PlannerTask>().CountAsync(t => t.CalendarId == calendarId, CancellationToken))
            .Should().Be(2, "the refused block must not have been written");
    }

    [Fact(DisplayName = "Create: foreground and background blocks have a limit each")]
    public async Task Create_CountsEachKindOnItsOwn()
    {
        var (activityId, calendarId) = await SeedDayAsync("lanes-kinds");

        (await CreateAsync(activityId, calendarId, (9, 0), (10, 0))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await CreateAsync(activityId, calendarId, (9, 0), (10, 0))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await CreateAsync(activityId, calendarId, (9, 0), (10, 0), background: true)).StatusCode.Should().Be(HttpStatusCode.Created,
            "two foreground blocks do not use up the background lanes");
        (await CreateAsync(activityId, calendarId, (9, 0), (10, 0), background: true)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await CreateAsync(activityId, calendarId, (9, 0), (10, 0), background: true)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact(DisplayName = "Span patch: dragging into a two-block stretch is a 409, beside one block it saves")]
    public async Task SpanPatch_RefusesAThirdOverlappingBlock()
    {
        var (activityId, calendarId) = await SeedDayAsync("lanes-span");
        await using var db = CreateDbContext();
        await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: CancellationToken);
        await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(9, 30), ct: CancellationToken);
        var moving = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(11, 0), new TimeOnly(11, 30), ct: CancellationToken);

        (await PatchSpanAsync(moving, (9, 15), (9, 45))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await LoadAsync(moving)).StartTime.Should().Be(new TimeOnly(11, 0), "the refused patch must not have been applied");

        (await PatchSpanAsync(moving, (9, 30), (10, 0))).StatusCode.Should().Be(HttpStatusCode.NoContent,
            "from 09:30 only the 09:00-10:00 block is still there");
    }

    [Fact(DisplayName = "Update: moving onto two blocks is a 409; editing a block that already clashes is not")]
    public async Task Update_JudgesOnlyAMove()
    {
        var (activityId, calendarId) = await SeedDayAsync("lanes-update");
        await using var db = CreateDbContext();
        // Three at once, as a template apply or a row from before the rule can leave a day.
        var clashing = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: CancellationToken);
        await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: CancellationToken);
        await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: CancellationToken);
        var elsewhere = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(14, 0), new TimeOnly(15, 0), ct: CancellationToken);

        (await PutAsync(clashing, activityId, calendarId, (9, 0), (10, 0), notes: "still editable")).StatusCode
            .Should().Be(HttpStatusCode.OK, "a write that does not move the block is not judged");
        (await LoadAsync(clashing)).Notes.Should().Be("still editable");

        (await PutAsync(elsewhere, activityId, calendarId, (9, 30), (10, 30))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await LoadAsync(elsewhere)).StartTime.Should().Be(new TimeOnly(14, 0));
    }

    private async Task<(long ActivityId, long CalendarId)> SeedDayAsync(string name)
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, name, ct: CancellationToken);
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 5, 3), ct: CancellationToken);
        return (activityId, calendarId);
    }

    private Task<HttpResponseMessage> CreateAsync(long activityId, long calendarId, (int H, int M) start, (int H, int M) end, bool background = false) =>
        CreateClient().PostAsJsonAsync("api/planner-task", new
        {
            StartTime = new { Hours = start.H, Minutes = start.M },
            EndTime = new { Hours = end.H, Minutes = end.M },
            IsBackground = background,
            ActivityId = activityId,
            Status = "NotStarted",
            CalendarId = calendarId
        }, JsonOpts, CancellationToken);

    private Task<HttpResponseMessage> PutAsync(long id, long activityId, long calendarId, (int H, int M) start, (int H, int M) end, string? notes = null) =>
        CreateClient().PutAsJsonAsync($"api/planner-task/{id}", new
        {
            StartTime = new { Hours = start.H, Minutes = start.M },
            EndTime = new { Hours = end.H, Minutes = end.M },
            IsBackground = false,
            ActivityId = activityId,
            Status = "NotStarted",
            CalendarId = calendarId,
            Notes = notes
        }, JsonOpts, CancellationToken);

    private Task<HttpResponseMessage> PatchSpanAsync(long id, (int H, int M) start, (int H, int M) end) =>
        CreateClient().PatchAsJsonAsync($"api/planner-task/{id}", new
        {
            StartTime = new { Hours = start.H, Minutes = start.M },
            EndTime = new { Hours = end.H, Minutes = end.M }
        }, JsonOpts, CancellationToken);

    private async Task<PlannerTask> LoadAsync(long id)
    {
        await using var db = CreateDbContext();
        return await db.Set<PlannerTask>().AsNoTracking().SingleAsync(t => t.Id == id, CancellationToken);
    }
}
