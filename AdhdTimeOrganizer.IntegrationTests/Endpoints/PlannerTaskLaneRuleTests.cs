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
/// the point of the lanes — and a write is judged only on the minutes it newly covers, so a day that already holds
/// three still lets the user edit, shrink or extend-into-free-time a block among them.
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

    [Theory(DisplayName = "Newly covered: a move on the same day and kind adds only the minutes the old span did not have")]
    [InlineData(9, 0, 10, 0, 9, 0, 10, 30, "10:00-10:30")] // extension
    [InlineData(9, 0, 10, 0, 9, 30, 10, 30, "10:00-10:30")] // drag down, overlapping
    [InlineData(9, 0, 10, 0, 8, 30, 9, 30, "08:30-09:00")] // drag up, overlapping
    [InlineData(9, 0, 10, 0, 11, 0, 12, 0, "11:00-12:00")] // drag clear of the old span
    [InlineData(9, 0, 10, 0, 8, 0, 9, 0, "08:00-09:00")] // drag up, touching the old start
    [InlineData(9, 0, 10, 0, 8, 30, 10, 30, "08:30-09:00,10:00-10:30")] // growth at both ends
    [InlineData(9, 0, 10, 0, 9, 15, 9, 45, "")] // shrink
    [InlineData(9, 0, 10, 0, 9, 0, 10, 0, "")] // unchanged
    public void NewlyCovered_IsTheNewSpanMinusTheOld(int os, int osm, int oe, int oem, int ns, int nsm, int ne, int nem, string expected)
    {
        var stretches = PlannerTaskLaneRule.NewlyCovered(
            (new TimeOnly(os, osm), new TimeOnly(oe, oem)), new TimeOnly(ns, nsm), new TimeOnly(ne, nem));
        string.Join(",", stretches.Select(s => $"{s.Start:HH:mm}-{s.End:HH:mm}")).Should().Be(expected);
    }

    [Fact(DisplayName = "Newly covered: a new block, or one moved to another day or kind, covers its whole span")]
    public void NewlyCovered_WithoutAnOldSpan_IsTheWholeSpan() =>
        PlannerTaskLaneRule.NewlyCovered(null, new TimeOnly(9, 0), new TimeOnly(10, 0))
            .Should().ContainSingle().Which.Should().Be((new TimeOnly(9, 0), new TimeOnly(10, 0)));

    [Fact(DisplayName = "Extend: a block among three extends into free time, but not into a stretch two others hold")]
    public async Task Extend_AClashingBlock_IsJudgedOnlyOnTheAddedStretch()
    {
        var (activityId, calendarId) = await SeedDayAsync("lanes-extend");
        await using var db = CreateDbContext();
        // Three at once, as a template apply or copy-to-days can leave a day, and a full pair later on.
        var first = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: CancellationToken);
        var second = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: CancellationToken);
        var third = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: CancellationToken);
        await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(11, 0), new TimeOnly(12, 0), ct: CancellationToken);
        await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(11, 0), new TimeOnly(12, 0), ct: CancellationToken);

        (await PutAsync(first, activityId, calendarId, (9, 0), (10, 30))).StatusCode.Should().Be(HttpStatusCode.OK,
            "only 10:00-10:30 is new, and nothing else is there; the clash it already sits in is not this write's");
        (await LoadAsync(first)).EndTime.Should().Be(new TimeOnly(10, 30));

        (await PutAsync(second, activityId, calendarId, (9, 0), (11, 30))).StatusCode.Should().Be(HttpStatusCode.Conflict,
            "11:00-11:30 is new and both lanes are already taken there");
        (await LoadAsync(second)).EndTime.Should().Be(new TimeOnly(10, 0), "the refused update must not have been applied");

        (await PatchSpanAsync(second, (9, 0), (10, 30))).StatusCode.Should().Be(HttpStatusCode.NoContent,
            "beside the first block's extension, 10:00-10:30 holds one other");
        (await LoadAsync(second)).EndTime.Should().Be(new TimeOnly(10, 30));

        (await PatchSpanAsync(third, (9, 0), (10, 15))).StatusCode.Should().Be(HttpStatusCode.Conflict,
            "10:00-10:15 is new and the two extensions already hold it");
        (await LoadAsync(third)).EndTime.Should().Be(new TimeOnly(10, 0), "the refused patch must not have been applied");
    }

    [Fact(DisplayName = "Shrink: a block among three can shrink, by the span patch and by the PUT")]
    public async Task Shrink_OfAClashingBlock_Saves()
    {
        var (activityId, calendarId) = await SeedDayAsync("lanes-shrink");
        await using var db = CreateDbContext();
        var first = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: CancellationToken);
        var second = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: CancellationToken);
        await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: CancellationToken);

        (await PatchSpanAsync(first, (9, 15), (9, 45))).StatusCode.Should().Be(HttpStatusCode.NoContent,
            "a shrink covers no minute it did not already cover");
        (await LoadAsync(first)).StartTime.Should().Be(new TimeOnly(9, 15));

        (await PutAsync(second, activityId, calendarId, (9, 0), (9, 30))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoadAsync(second)).EndTime.Should().Be(new TimeOnly(9, 30));
    }

    [Fact(DisplayName = "Drag: a block among three is judged on the minutes it slides onto")]
    public async Task Drag_OfAClashingBlock_IsJudgedOnItsNewMinutes()
    {
        var (activityId, calendarId) = await SeedDayAsync("lanes-drag");
        await using var db = CreateDbContext();
        var moving = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: CancellationToken);
        await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: CancellationToken);
        await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: CancellationToken);
        await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(10, 0), new TimeOnly(11, 0), ct: CancellationToken);
        await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(10, 0), new TimeOnly(11, 0), ct: CancellationToken);

        (await PatchSpanAsync(moving, (9, 30), (10, 30))).StatusCode.Should().Be(HttpStatusCode.Conflict,
            "10:00-10:30 is new and the 10:00-11:00 pair holds it");
        (await LoadAsync(moving)).StartTime.Should().Be(new TimeOnly(9, 0), "the refused patch must not have been applied");

        (await PatchSpanAsync(moving, (8, 30), (9, 30))).StatusCode.Should().Be(HttpStatusCode.NoContent,
            "dragged up, only 08:30-09:00 is new, and it is free");
        (await LoadAsync(moving)).StartTime.Should().Be(new TimeOnly(8, 30));
    }

    [Fact(DisplayName = "Move: to another day or the other kind, the whole span is new even when it is unchanged")]
    public async Task Move_ToAnotherDayOrKind_IsJudgedOnItsWholeSpan()
    {
        var (activityId, calendarId) = await SeedDayAsync("lanes-move");
        long otherDayId;
        long moving;
        await using (var db = CreateDbContext())
        {
            otherDayId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 5, 4), ct: CancellationToken);
            moving = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: CancellationToken);
            await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, otherDayId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: CancellationToken);
            await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, otherDayId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: CancellationToken);
        }
        (await CreateAsync(activityId, calendarId, (9, 0), (10, 0), background: true)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await CreateAsync(activityId, calendarId, (9, 0), (10, 0), background: true)).StatusCode.Should().Be(HttpStatusCode.Created);

        (await PutAsync(moving, activityId, otherDayId, (9, 0), (10, 0))).StatusCode.Should().Be(HttpStatusCode.Conflict,
            "on the other day all of 09:00-10:00 is new, and both lanes are taken there");
        (await LoadAsync(moving)).CalendarId.Should().Be(calendarId, "the refused move must not have been applied");

        (await PutAsync(moving, activityId, calendarId, (9, 0), (10, 0), background: true)).StatusCode.Should().Be(HttpStatusCode.Conflict,
            "as a background block it covers all of 09:00-10:00 anew, where both background lanes are taken");
        (await LoadAsync(moving)).IsBackground.Should().BeFalse();
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

    private Task<HttpResponseMessage> PutAsync(
        long id, long activityId, long calendarId, (int H, int M) start, (int H, int M) end, string? notes = null, bool background = false) =>
        CreateClient().PutAsJsonAsync($"api/planner-task/{id}", new
        {
            StartTime = new { Hours = start.H, Minutes = start.M },
            EndTime = new { Hours = end.H, Minutes = end.M },
            IsBackground = background,
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
