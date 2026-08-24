using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The four batched calendar entry points that collapse the month view's per-day fan-out:
/// <c>calendar/task-summaries</c>, <c>calendar/apply-planner-template/batch</c>,
/// <c>planner-task/copy-to-days</c> and <c>calendar/day-type/batch</c>.
/// <para>
/// These assert on rows, not on routes, for the reason the root CLAUDE.md gives: every one of them answers
/// <c>200</c> with a per-day <c>succeeded</c> flag rather than an HTTP failure, so a batch that silently wrote
/// nothing, wrote to the wrong day, or reported success for a day it skipped is indistinguishable from a
/// working one at the status-code level. Each test therefore checks the response <i>and</i> the resulting
/// planner-task / calendar rows.
/// </para>
/// <para>
/// Every batch is also given one id belonging to <see cref="ReminderSeedHelper.OtherUserId"/>. That id must
/// come back <c>succeeded: false</c> and leave the other user's day untouched — these endpoints scope through
/// the host's global query filter alone, so a hand-written user predicate creeping in (or the filter being
/// lost) has to fail here.
/// </para>
/// </summary>
[Collection("Postgres")]
public class CalendarBatchEndpointTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    // ─── 1. calendar/task-summaries ──────────────────────────────────────────

    /// <summary>
    /// One request answers the whole visible range: tasks grouped by calendar id, ordered by start time,
    /// carrying only the five fields a cell draws. Days with no tasks are absent rather than empty, days
    /// outside the range are absent, and another user's day in the range is absent.
    /// </summary>
    [Fact]
    public async Task TaskSummaries_GroupsRangeByDay_OmittingEmptyDaysAndOtherUsers()
    {
        long dayWithTwo, dayWithOne, emptyDay, outsideRange, theirDay;
        long activityId;

        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Deep work");

            dayWithTwo = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 4, 5));
            dayWithOne = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 4, 6));
            emptyDay = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 4, 7));
            outsideRange = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 4, 20));

            // Seeded out of chronological order — the endpoint must order, not the insert.
            await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, dayWithTwo, new TimeOnly(14, 0), new TimeOnly(15, 30));
            await SeedBackgroundTaskAsync(db, activityId, dayWithTwo, new TimeOnly(9, 0), new TimeOnly(10, 30));
            await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, dayWithOne, new TimeOnly(8, 0), new TimeOnly(8, 45));
            await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, outsideRange, new TimeOnly(9, 0), new TimeOnly(10, 0));

            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            var theirActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "theirs", ReminderSeedHelper.OtherUserId);
            theirDay = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 4, 6), ReminderSeedHelper.OtherUserId);
            await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, theirActivityId, theirDay, new TimeOnly(11, 0), new TimeOnly(12, 0), ReminderSeedHelper.OtherUserId);
        }

        var response = await CreateClient().PostAsJsonAsync("api/calendar/task-summaries", new
        {
            From = new DateOnly(2027, 4, 1),
            Until = new DateOnly(2027, 4, 10)
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CalendarTaskSummariesResponse>(JsonOpts, CancellationToken);

        body!.Days.Select(d => d.CalendarId).Should().Equal([dayWithTwo, dayWithOne],
            "only days inside the range that have tasks appear, ordered by date — the empty day, the day outside the range and the other user's day are all absent");
        body.Days.Should().NotContain(d => d.CalendarId == emptyDay || d.CalendarId == outsideRange || d.CalendarId == theirDay);

        var first = body.Days.Single(d => d.CalendarId == dayWithTwo);
        first.Tasks.Select(t => (t.StartTime.Hours, t.StartTime.Minutes, t.EndTime.Hours, t.EndTime.Minutes))
            .Should().Equal([(9, 0, 10, 30), (14, 0, 15, 30)], "tasks are ordered by start time within a day");
        first.Tasks[0].IsBackground.Should().BeTrue();
        first.Tasks[1].IsBackground.Should().BeFalse();
        first.Tasks.Should().OnlyContain(t => t.ActivityName == "Deep work" && t.Color == "#123456");
        first.Tasks.Should().OnlyContain(t => t.Id > 0, "the id is the tooltip list's Vue key");
    }

    /// <summary>An inverted range is rejected outright rather than answered with an empty day list.</summary>
    [Fact]
    public async Task TaskSummaries_RejectsInvertedRange()
    {
        var response = await CreateClient().PostAsJsonAsync("api/calendar/task-summaries", new
        {
            From = new DateOnly(2027, 4, 10),
            Until = new DateOnly(2027, 4, 1)
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ─── 2. calendar/apply-planner-template/batch ────────────────────────────

    /// <summary>
    /// The template lands on every day the caller owns, with the conflict resolution applied per day against
    /// that day's own tasks; an unreachable day fails on its own. The template's usage count must count the
    /// days it actually applied to — not the days requested.
    /// </summary>
    [Fact]
    public async Task ApplyTemplateBatch_AppliesPerDay_AndFailsOnlyTheUnreachableDay()
    {
        long activityId, emptyDay, dayWithConflict, theirDay, templateId;

        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "batch-apply");
            templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db);

            emptyDay = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 5, 3));
            dayWithConflict = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 5, 4));
            await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, dayWithConflict, new TimeOnly(9, 30), new TimeOnly(10, 30));

            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            theirDay = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 5, 5), ReminderSeedHelper.OtherUserId);
        }

        var response = await CreateClient().PostAsJsonAsync("api/calendar/apply-planner-template/batch", new
        {
            TemplateId = templateId,
            CalendarIds = new[] { emptyDay, dayWithConflict, theirDay },
            ConflictResolution = "Ignore",
            TasksFromTemplate = new[]
            {
                new
                {
                    StartTime = new { Hours = 9, Minutes = 0 },
                    EndTime = new { Hours = 10, Minutes = 0 },
                    IsBackground = false,
                    ActivityId = activityId,
                    Status = "NotStarted"
                }
            }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CalendarBatchResultResponse>(JsonOpts, CancellationToken);

        body!.Results.Should().Equal(
        [
            new CalendarBatchResult { CalendarId = emptyDay, Succeeded = true },
            new CalendarBatchResult { CalendarId = dayWithConflict, Succeeded = true },
            new CalendarBatchResult { CalendarId = theirDay, Succeeded = false }
        ], "one result per requested id, in request order, and another user's day is unreachable rather than an error for the batch");

        await using (var db = CreateDbContext())
        {
            (await TaskSpansAsync(db, emptyDay)).Should().Equal([(new TimeOnly(9, 0), new TimeOnly(10, 0))]);

            (await TaskSpansAsync(db, dayWithConflict))
                .Should().Equal([(new TimeOnly(9, 30), new TimeOnly(10, 30))],
                    "Ignore is resolved against THIS day's tasks — the template task overlaps here and is dropped, even though the same task landed on the other day");

            (await TaskSpansAsync(db, theirDay)).Should().BeEmpty("the other user's day must not be written");
            var theirCalendar = await db.Set<Calendar>().IgnoreQueryFilters().SingleAsync(c => c.Id == theirDay, CancellationToken);
            theirCalendar.AppliedTemplateId.Should().BeNull();

            var template = await db.Set<TaskPlannerDayTemplate>().SingleAsync(t => t.Id == templateId, CancellationToken);
            template.UsageCount.Should().Be(2, "the two days it actually applied to, not the three requested");
            template.LastUsedAt.Should().NotBeNull();

            foreach (var appliedDay in new[] { emptyDay, dayWithConflict })
            {
                var calendar = await db.Set<Calendar>().SingleAsync(c => c.Id == appliedDay, CancellationToken);
                calendar.AppliedTemplateId.Should().Be(templateId);
            }
        }
    }

    /// <summary>A duplicate id is rejected: the response promises one result per requested id, in order.</summary>
    [Fact]
    public async Task ApplyTemplateBatch_RejectsDuplicateCalendarIds()
    {
        long activityId, calendarId, templateId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "batch-apply-dupe");
            templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db);
            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 5, 6));
        }

        var response = await CreateClient().PostAsJsonAsync("api/calendar/apply-planner-template/batch", new
        {
            TemplateId = templateId,
            CalendarIds = new[] { calendarId, calendarId },
            ConflictResolution = "Ignore",
            TasksFromTemplate = new[]
            {
                new
                {
                    StartTime = new { Hours = 9, Minutes = 0 },
                    EndTime = new { Hours = 10, Minutes = 0 },
                    IsBackground = false,
                    ActivityId = activityId,
                    Status = "NotStarted"
                }
            }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using (var db = CreateDbContext())
            (await TaskSpansAsync(db, calendarId)).Should().BeEmpty("a rejected batch writes nothing at all");
    }

    // ─── 3. planner-task/copy-to-days ────────────────────────────────────────

    /// <summary>
    /// Every target day gets the source day's plan; the source is left alone. Execution state does not travel:
    /// a completed source task arrives as NotStarted with no actual times, which is the whole difference
    /// between copying a plan and copying a day's history.
    /// </summary>
    [Fact]
    public async Task CopyToDays_CopiesPlanWithoutExecutionState_AndFailsOnlyTheUnreachableDay()
    {
        long sourceDay, firstTarget, secondTarget, theirDay, completedTaskId;

        await using (var db = CreateDbContext())
        {
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "copy-source");
            var importanceId = await PlanningTestSeedHelper.SeedTaskImportanceAsync(db, 3);

            sourceDay = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 6, 7));
            firstTarget = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 6, 8));
            secondTarget = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 6, 9));

            completedTaskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(
                db, activityId, sourceDay, new TimeOnly(9, 0), new TimeOnly(10, 0),
                status: PlannerTaskStatus.Completed, importanceId: importanceId, ct: CancellationToken);

            var completed = await db.Set<PlannerTask>().SingleAsync(t => t.Id == completedTaskId, CancellationToken);
            completed.ActualStartTime = new TimeOnly(9, 5);
            completed.ActualEndTime = new TimeOnly(9, 55);
            completed.Notes = "carried over";
            completed.Location = "Office";
            await db.SaveChangesAsync(CancellationToken);

            await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, sourceDay, new TimeOnly(14, 0), new TimeOnly(15, 0));

            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            theirDay = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 6, 10), ReminderSeedHelper.OtherUserId);
        }

        var response = await CreateClient().PostAsJsonAsync("api/planner-task/copy-to-days", new
        {
            SourceCalendarId = sourceDay,
            TargetCalendarIds = new[] { firstTarget, theirDay, secondTarget }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CalendarBatchResultResponse>(JsonOpts, CancellationToken);

        body!.Results.Should().Equal(
        [
            new CalendarBatchResult { CalendarId = firstTarget, Succeeded = true },
            new CalendarBatchResult { CalendarId = theirDay, Succeeded = false },
            new CalendarBatchResult { CalendarId = secondTarget, Succeeded = true }
        ], "a failing day in the middle must not stop the days after it");

        await using (var db = CreateDbContext())
        {
            foreach (var target in new[] { firstTarget, secondTarget })
            {
                var copies = await db.Set<PlannerTask>().Where(t => t.CalendarId == target).OrderBy(t => t.StartTime).ToListAsync(CancellationToken);

                copies.Select(t => (t.StartTime, t.EndTime))
                    .Should().Equal([(new TimeOnly(9, 0), new TimeOnly(10, 0)), (new TimeOnly(14, 0), new TimeOnly(15, 0))]);
                copies.Should().OnlyContain(t => t.Status == PlannerTaskStatus.NotStarted,
                    "a copy is of the plan; the source's completion is not a fact about the target day");
                copies.Should().OnlyContain(t => t.ActualStartTime == null && t.ActualEndTime == null);
                copies.Should().OnlyContain(t => t.UserId == UserId);

                var copyOfCompleted = copies[0];
                copyOfCompleted.Notes.Should().Be("carried over");
                copyOfCompleted.Location.Should().Be("Office");
                copyOfCompleted.ImportanceId.Should().NotBeNull("importance is part of the plan");
            }

            (await TaskSpansAsync(db, theirDay)).Should().BeEmpty("the other user's day must not be written");

            var source = await db.Set<PlannerTask>().SingleAsync(t => t.Id == completedTaskId, CancellationToken);
            source.Status.Should().Be(PlannerTaskStatus.Completed, "the source day is read, never modified");
            source.ActualStartTime.Should().Be(new TimeOnly(9, 5));
        }
    }

    /// <summary>Another user's day cannot be used as a copy source: the read is scoped, so it is a 404.</summary>
    [Fact]
    public async Task CopyToDays_RejectsAnotherUsersSourceDay()
    {
        long theirDay, myDay;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            var theirActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "their-source", ReminderSeedHelper.OtherUserId);
            theirDay = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 6, 11), ReminderSeedHelper.OtherUserId);
            await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, theirActivityId, theirDay, new TimeOnly(9, 0), new TimeOnly(10, 0), ReminderSeedHelper.OtherUserId);

            myDay = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 6, 12));
        }

        var response = await CreateClient().PostAsJsonAsync("api/planner-task/copy-to-days", new
        {
            SourceCalendarId = theirDay,
            TargetCalendarIds = new[] { myDay }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using (var db = CreateDbContext())
            (await TaskSpansAsync(db, myDay)).Should().BeEmpty("nothing of another user's day may leak into mine");
    }

    // ─── 4. calendar/day-type/batch ──────────────────────────────────────────

    /// <summary>
    /// The day type is written on every reachable day and nothing else on the row is touched — the batch
    /// carries only the one field, unlike the per-day PUT it replaces, which echoed the whole calendar back.
    /// </summary>
    [Fact]
    public async Task BatchDayType_WritesOnlyTheDayType()
    {
        long firstDay, secondDay, theirDay;

        await using (var db = CreateDbContext())
        {
            firstDay = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 7, 5));
            secondDay = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 7, 6));

            var first = await db.Set<Calendar>().SingleAsync(c => c.Id == firstDay, CancellationToken);
            first.Label = "Office";
            first.Notes = "keep me";
            await db.SaveChangesAsync(CancellationToken);

            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            theirDay = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 7, 7), ReminderSeedHelper.OtherUserId);
        }

        var response = await CreateClient().PatchAsJsonAsync("api/calendar/day-type/batch", new
        {
            CalendarIds = new[] { firstDay, secondDay, theirDay },
            DayType = "Vacation"
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CalendarBatchResultResponse>(JsonOpts, CancellationToken);

        body!.Results.Should().Equal(
        [
            new CalendarBatchResult { CalendarId = firstDay, Succeeded = true },
            new CalendarBatchResult { CalendarId = secondDay, Succeeded = true },
            new CalendarBatchResult { CalendarId = theirDay, Succeeded = false }
        ]);

        await using (var db = CreateDbContext())
        {
            var first = await db.Set<Calendar>().SingleAsync(c => c.Id == firstDay, CancellationToken);
            first.DayType.Should().Be(DayType.Vacation);
            first.Label.Should().Be("Office", "the batch carries only the day type — everything else on the row is left alone");
            first.Notes.Should().Be("keep me");
            first.WakeUpTime.Should().Be(new TimeOnly(7, 0));
            first.BedTime.Should().Be(new TimeOnly(23, 0));

            (await db.Set<Calendar>().SingleAsync(c => c.Id == secondDay, CancellationToken)).DayType.Should().Be(DayType.Vacation);

            var theirs = await db.Set<Calendar>().IgnoreQueryFilters().SingleAsync(c => c.Id == theirDay, CancellationToken);
            theirs.DayType.Should().Be(DayType.Workday, "the other user's day must not be written");
        }
    }

    /// <summary>
    /// The weekday rule that <c>UpdateCalendarEndpoint</c> answers with a 400 is, here, one day's failure.
    /// Selecting a week and marking it Workday must not be cancelled by the Saturday in it.
    /// </summary>
    [Fact]
    public async Task BatchDayType_WeekendRuleFailsOneDayWithoutFailingTheBatch()
    {
        long monday, saturday;

        await using (var db = CreateDbContext())
        {
            monday = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 5, 3), UserId, CancellationToken, DayType.Vacation);
            saturday = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 5, 8), UserId, CancellationToken, DayType.Vacation);
        }

        var response = await CreateClient().PatchAsJsonAsync("api/calendar/day-type/batch", new
        {
            CalendarIds = new[] { monday, saturday },
            DayType = "Workday"
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CalendarBatchResultResponse>(JsonOpts, CancellationToken);

        body!.Results.Should().Equal(
        [
            new CalendarBatchResult { CalendarId = monday, Succeeded = true },
            new CalendarBatchResult { CalendarId = saturday, Succeeded = false }
        ]);

        await using (var db = CreateDbContext())
        {
            (await db.Set<Calendar>().SingleAsync(c => c.Id == monday, CancellationToken)).DayType.Should().Be(DayType.Workday);
            (await db.Set<Calendar>().SingleAsync(c => c.Id == saturday, CancellationToken)).DayType
                .Should().Be(DayType.Vacation, "'Workday' is impossible on a Saturday, and that day alone keeps its old type");
        }
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private async Task<List<(TimeOnly Start, TimeOnly End)>> TaskSpansAsync(DbContext db, long calendarId) =>
        (await db.Set<PlannerTask>()
            .IgnoreQueryFilters()
            .Where(t => t.CalendarId == calendarId)
            .OrderBy(t => t.StartTime)
            .Select(t => new { t.StartTime, t.EndTime })
            .ToListAsync(CancellationToken))
        .Select(t => (t.StartTime, t.EndTime))
        .ToList();

    private async Task<long> SeedBackgroundTaskAsync(DbContext db, long activityId, long calendarId, TimeOnly start, TimeOnly end)
    {
        var task = new PlannerTask
        {
            UserId = UserId,
            ActivityId = activityId,
            CalendarId = calendarId,
            StartTime = start,
            EndTime = end,
            IsBackground = true,
            Status = PlannerTaskStatus.NotStarted
        };
        db.Set<PlannerTask>().Add(task);
        await db.SaveChangesAsync(CancellationToken);
        return task.Id;
    }
}
