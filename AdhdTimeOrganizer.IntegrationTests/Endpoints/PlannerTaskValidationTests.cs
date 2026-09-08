using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-5 — the rules of <c>PlannerTaskValidator</c>, which guards <b>both</b>
/// <c>POST /api/planner-task</c> and <c>PUT /api/planner-task/{id}</c>: the main write path for the
/// slice's central entity. Exactly one of its rules was covered before this file existed
/// (<see cref="LazyCalendarCreationTests.ATaskNamingItsDayTwice_OrNotAtAll_IsRejected"/>).
/// <para>
/// The ordering rule (<c>StartTime &lt; EndTime</c>) on <c>POST</c>/<c>PUT /api/planner-task</c> is
/// <b>not</b> repeated here — it is
/// <see cref="PlannerTaskStatusAndSpanTests.CreatingAnOvernightTask_IsRejected"/>, which covers create,
/// the equal-times case, the full update, and the span patch in one place. What that test does not
/// reach is the other two validators carrying the same rule, so those are below.
/// </para>
/// </summary>
[Collection("Postgres")]
public class PlannerTaskValidationTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    // ─── PlannerTaskValidator's field rules ──────────────────────────────────

    /// <summary>
    /// The control for every rejection below. Without it a theory case could be passing for a reason
    /// that has nothing to do with the field it names — a missing activity, an unowned calendar, a
    /// serializer mismatch — and the whole file would be green against a route that rejects everything.
    /// </summary>
    [Fact(DisplayName = "The unmutated body this file mutates is accepted")]
    public async Task AWellFormedPlannerTask_IsAccepted()
    {
        var (activityId, calendarId) = await SeedActivityAndDayAsync("validator-control");

        var response = await PostTaskAsync(ValidTaskBody(activityId, calendarId));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>
    /// One case per rule in <c>PlannerTaskValidator</c>, each mutating exactly one field of the body the
    /// control above proves is otherwise accepted.
    /// <para>
    /// The two day-naming cases (<c>both</c> / <c>neither</c>) are also covered by
    /// <see cref="LazyCalendarCreationTests.ATaskNamingItsDayTwice_OrNotAtAll_IsRejected"/>; they are
    /// repeated here because they are two of the ten rules this file is the inventory of, and a rule
    /// covered only in a file about lazy calendar creation is a rule the next reader of the validator
    /// will believe is uncovered.
    /// </para>
    /// </summary>
    [Theory(DisplayName = "PlannerTaskValidator rejects the malformed field named by the case")]
    [InlineData("startTime.hours=24")]
    [InlineData("startTime.minutes=60")]
    [InlineData("endTime.hours=-1")]
    [InlineData("both-calendarId-and-date")]
    [InlineData("neither-calendarId-nor-date")]
    [InlineData("calendarId=0")]
    [InlineData("activityId=0")]
    [InlineData("importanceId=0")]
    [InlineData("todolistId=0")]
    [InlineData("location=201chars")]
    [InlineData("notes=1001chars")]
    [InlineData("actualStartTime+NotStarted")]
    [InlineData("actualStartTime+Cancelled")]
    public async Task AMalformedPlannerTask_IsRejected(string mutation)
    {
        var (activityId, calendarId) = await SeedActivityAndDayAsync($"validator-{mutation}");
        var body = ValidTaskBody(activityId, calendarId);

        switch (mutation)
        {
            case "startTime.hours=24":
                body["startTime"] = new { hours = 24, minutes = 0 };
                break;
            case "startTime.minutes=60":
                body["startTime"] = new { hours = 9, minutes = 60 };
                break;
            case "endTime.hours=-1":
                body["endTime"] = new { hours = -1, minutes = 0 };
                break;
            case "both-calendarId-and-date":
                body["date"] = new DateOnly(2029, 3, 5);
                break;
            case "neither-calendarId-nor-date":
                body["calendarId"] = null;
                break;
            case "calendarId=0":
                body["calendarId"] = 0L;
                break;
            case "activityId=0":
                body["activityId"] = 0L;
                break;
            case "importanceId=0":
                body["importanceId"] = 0L;
                break;
            case "todolistId=0":
                body["todolistId"] = 0L;
                break;
            case "location=201chars":
                body["location"] = new string('x', 201);
                break;
            case "notes=1001chars":
                body["notes"] = new string('x', 1001);
                break;
            case "actualStartTime+NotStarted":
                body["actualStartTime"] = new { hours = 9, minutes = 5 };
                break;
            case "actualStartTime+Cancelled":
                body["actualStartTime"] = new { hours = 9, minutes = 5 };
                body["status"] = "Cancelled";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "Unknown mutation.");
        }

        var response = await PostTaskAsync(body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<PlannerTask>().IgnoreQueryFilters().AnyAsync(t => t.ActivityId == activityId, CancellationToken))
            .Should().BeFalse("a rejected create writes nothing — a 400 with a row behind it is the worse failure");
    }

    /// <summary>
    /// The control for the two <c>actualStartTime</c> rejections above: the field itself is accepted, and
    /// only the status combination is refused. Asserts the column, not the status code — the whole point
    /// of the field is that a task created already under way shows as started, which reads this column
    /// rather than the status.
    /// </summary>
    [Fact(DisplayName = "ActualStartTime is accepted with status InProgress and lands on the row")]
    public async Task ActualStartTime_WithInProgress_IsAcceptedAndPersisted()
    {
        var (activityId, calendarId) = await SeedActivityAndDayAsync("validator-actual-inprogress");
        var body = ValidTaskBody(activityId, calendarId);
        body["status"] = "InProgress";
        body["actualStartTime"] = new { hours = 9, minutes = 5 };

        var response = await PostTaskAsync(body);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var verifyDb = CreateDbContext();
        var task = await verifyDb.Set<PlannerTask>().SingleAsync(t => t.ActivityId == activityId, CancellationToken);
        task.Status.Should().Be(PlannerTaskStatus.InProgress);
        task.ActualStartTime.Should().Be(new TimeOnly(9, 5));
    }

    // ─── the ordering rule on the other two write paths (CQ-14) ──────────────

    /// <summary>
    /// <c>CQ-14</c>'s other two halves. <c>PlannerTaskValidator</c>, <c>RepeatingPlannerTaskValidator</c>
    /// and <c>TemplatePlannerTaskValidator</c> all gained the <c>StartTime &lt; EndTime</c> rule on
    /// 2026-09-08; only the first of the three is exercised anywhere
    /// (<see cref="PlannerTaskStatusAndSpanTests.CreatingAnOvernightTask_IsRejected"/>), which is how the
    /// gap stayed easy to miss for as long as it did — <c>domain-map.md</c>'s invariant table said the rule
    /// held on every planner-task validator while it held on exactly one (<c>DOC-1</c>; the line is correct
    /// now, and this test is what keeps it true of the two validators nothing else touches).
    /// <para>
    /// It matters on both: a recurrence materialises into ordinary planner tasks on every day it fires, and
    /// a template line is stamped onto a day as one — so a negative-duration row accepted here seeds the
    /// shape <c>TasksOverlap</c> misreads and <c>PlannerTemplateApplier</c> refuses to carve, once per
    /// occurrence rather than once.
    /// </para>
    /// </summary>
    [Theory(DisplayName = "A recurrence cannot be created with EndTime <= StartTime")]
    [InlineData(23, 0, 1, 0)]
    [InlineData(9, 0, 9, 0)]
    public async Task ARepeatingTaskEndingBeforeItStarts_IsRejected(int startHour, int startMinute, int endHour, int endMinute)
    {
        var (activityId, importanceId) = await SeedActivityAndImportanceAsync($"repeating-span-{startHour}-{endHour}");

        var response = await CreateUserRoleClient().PostAsJsonAsync("api/repeating-planner-task", new
        {
            StartTime = new { Hours = startHour, Minutes = startMinute },
            EndTime = new { Hours = endHour, Minutes = endMinute },
            IsBackground = false,
            ActivityId = activityId,
            // RepeatingPlannerTaskValidator requires ImportanceId, unlike PlannerTaskValidator where it is
            // optional — a genuine asymmetry between the two, not something to work around here.
            ImportanceId = importanceId,
            IsActive = true,
            RecurrenceType = "DayOfWeek",
            ScheduledDays = new[] { "Monday" }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "RepeatingPlannerTaskValidator carries the same strictly-before rule as PlannerTaskValidator");

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<RepeatingPlannerTask>().IgnoreQueryFilters().AnyAsync(t => t.ActivityId == activityId, CancellationToken))
            .Should().BeFalse();
    }

    /// <summary>The control for the theory above: the same body with an ordinary span is accepted.</summary>
    [Fact(DisplayName = "An ordinary recurrence is still accepted")]
    public async Task AWellFormedRecurrence_IsAccepted()
    {
        var (activityId, importanceId) = await SeedActivityAndImportanceAsync("repeating-span-control");

        var response = await CreateUserRoleClient().PostAsJsonAsync("api/repeating-planner-task", new
        {
            StartTime = new { Hours = 9, Minutes = 0 },
            EndTime = new { Hours = 10, Minutes = 0 },
            IsBackground = false,
            ActivityId = activityId,
            ImportanceId = importanceId,
            IsActive = true,
            RecurrenceType = "DayOfWeek",
            ScheduledDays = new[] { "Monday" }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory(DisplayName = "A template line cannot be created with EndTime <= StartTime")]
    [InlineData(23, 0, 1, 0)]
    [InlineData(9, 0, 9, 0)]
    public async Task ATemplateLineEndingBeforeItStarts_IsRejected(int startHour, int startMinute, int endHour, int endMinute)
    {
        long activityId, templateId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"template-span-{startHour}-{endHour}", ct: CancellationToken);
            templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, ct: CancellationToken);
        }

        var response = await CreateUserRoleClient().PostAsJsonAsync("api/template-planner-task", new
        {
            TemplateId = templateId,
            ActivityId = activityId,
            StartTime = new { Hours = startHour, Minutes = startMinute },
            EndTime = new { Hours = endHour, Minutes = endMinute },
            IsBackground = false
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "TemplatePlannerTaskValidator carries the same strictly-before rule as PlannerTaskValidator");

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<TemplatePlannerTask>().IgnoreQueryFilters().AnyAsync(t => t.ActivityId == activityId, CancellationToken))
            .Should().BeFalse();
    }

    /// <summary>The control for the theory above.</summary>
    [Fact(DisplayName = "An ordinary template line is still accepted")]
    public async Task AWellFormedTemplateLine_IsAccepted()
    {
        long activityId, templateId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "template-span-control", ct: CancellationToken);
            templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, ct: CancellationToken);
        }

        var response = await CreateUserRoleClient().PostAsJsonAsync("api/template-planner-task", new
        {
            TemplateId = templateId,
            ActivityId = activityId,
            StartTime = new { Hours = 9, Minutes = 0 },
            EndTime = new { Hours = 10, Minutes = 0 },
            IsBackground = false
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ─── the two omitted-field conventions on PlannerTaskRequest.UpdateEntity ─

    /// <summary>
    /// <c>PlannerTaskRequest.UpdateEntity</c> deliberately does not touch <c>ActualStartTime</c>
    /// (<c>PlannerTaskRequest:44-48</c>): a full <c>PUT</c> that omits the field must not wipe the start
    /// time of a task already under way, which is every existing update caller — the day-planner dialog and
    /// the drag-to-move path do not send it.
    /// <para>
    /// Nothing covered this, and it is exactly what a mapping refactor breaks: adding the one obvious line
    /// to <c>UpdateEntity</c> silently un-starts every task the user drags, with no error anywhere. The
    /// <c>EndTime</c> assertion is what stops this passing because the update never landed at all.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "A full PUT that omits ActualStartTime leaves a started task started")]
    public async Task UpdatingATaskWithoutActualStartTime_LeavesItAlone()
    {
        var (activityId, calendarId) = await SeedActivityAndDayAsync("update-keeps-actual-start");

        long taskId;
        await using (var db = CreateDbContext())
        {
            var task = new PlannerTask
            {
                UserId = UserId,
                ActivityId = activityId,
                CalendarId = calendarId,
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(10, 0),
                IsBackground = false,
                Status = PlannerTaskStatus.InProgress,
                ActualStartTime = new TimeOnly(9, 5)
            };
            db.Set<PlannerTask>().Add(task);
            await db.SaveChangesAsync(CancellationToken);
            taskId = task.Id;
        }

        var response = await CreateUserRoleClient().PutAsJsonAsync($"api/planner-task/{taskId}", new
        {
            StartTime = new { Hours = 9, Minutes = 0 },
            EndTime = new { Hours = 11, Minutes = 0 },
            IsBackground = false,
            ActivityId = activityId,
            Status = "InProgress",
            CalendarId = calendarId
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verifyDb = CreateDbContext();
        var updated = await verifyDb.Set<PlannerTask>().SingleAsync(t => t.Id == taskId, CancellationToken);
        updated.EndTime.Should().Be(new TimeOnly(11, 0), "the update did land — otherwise the assertion below is vacuous");
        updated.ActualStartTime.Should().Be(new TimeOnly(9, 5),
            "UpdateEntity leaves ActualStartTime alone; PATCH /planner-task/{id}/status is what changes it");
    }

    /// <summary>
    /// The opposite convention on the same DTO: <c>UpdateEntity</c> assigns <c>TodolistItemId</c>
    /// unconditionally (<c>PlannerTaskRequest:82</c>), so a full <c>PUT</c> that omits <c>TodolistId</c>
    /// <b>unlinks</b> the task from its to-do item, while the same omission of <c>ActualStartTime</c> one
    /// line earlier preserves the value.
    /// <para>
    /// Green against current behaviour, and pinning it is the point rather than endorsing it: whether the
    /// asymmetry is intended has never been settled. For a full <c>PUT</c> "absent means null" is the
    /// defensible reading — it is how every other nullable field on this DTO behaves — but the same client
    /// argument that justifies the <c>ActualStartTime</c> carve-out applies here, since the drag-to-move
    /// path does not send <c>TodolistId</c> either. If it is settled the other way, flip
    /// <c>TodolistItemId.Should().BeNull()</c> to <c>Should().Be(todoItemId)</c> and guard the assignment
    /// in <c>UpdateEntity</c> the way the <c>CalendarId</c> line above it is guarded.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "A full PUT that omits TodolistId unlinks the task from its to-do item")]
    public async Task UpdatingATaskWithoutTodolistId_ClearsTheLink()
    {
        var (activityId, calendarId) = await SeedActivityAndDayAsync("update-clears-todolist");

        long taskId, todoItemId;
        await using (var db = CreateDbContext())
        {
            var priority = new TaskPriority
            {
                UserId = UserId,
                Text = $"Priority {Guid.NewGuid():N}",
                Color = "#445566",
                Priority = 1
            };
            db.Set<TaskPriority>().Add(priority);
            await db.SaveChangesAsync(CancellationToken);

            var item = new TodoListItem { UserId = UserId, ActivityId = activityId, TaskPriorityId = priority.Id };
            db.Set<TodoListItem>().Add(item);
            await db.SaveChangesAsync(CancellationToken);
            todoItemId = item.Id;

            var task = new PlannerTask
            {
                UserId = UserId,
                ActivityId = activityId,
                CalendarId = calendarId,
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(10, 0),
                IsBackground = false,
                Status = PlannerTaskStatus.NotStarted,
                TodolistItemId = todoItemId
            };
            db.Set<PlannerTask>().Add(task);
            await db.SaveChangesAsync(CancellationToken);
            taskId = task.Id;
        }

        var response = await CreateUserRoleClient().PutAsJsonAsync($"api/planner-task/{taskId}", new
        {
            StartTime = new { Hours = 9, Minutes = 0 },
            EndTime = new { Hours = 11, Minutes = 0 },
            IsBackground = false,
            ActivityId = activityId,
            Status = "NotStarted",
            CalendarId = calendarId
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verifyDb = CreateDbContext();
        var updated = await verifyDb.Set<PlannerTask>().SingleAsync(t => t.Id == taskId, CancellationToken);
        updated.EndTime.Should().Be(new TimeOnly(11, 0), "the update did land");
        updated.TodolistItemId.Should().BeNull(
            "UpdateEntity assigns TodolistItemId unconditionally — the opposite of the ActualStartTime line above it");

        // The item itself is untouched: this unlinks, it does not delete.
        (await verifyDb.Set<TodoListItem>().AnyAsync(i => i.Id == todoItemId, CancellationToken)).Should().BeTrue();
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The request shape every case above starts from, keyed exactly as it goes over the wire so a case
    /// can replace one field without rebuilding the rest.
    /// </summary>
    private static Dictionary<string, object?> ValidTaskBody(long activityId, long calendarId) => new()
    {
        ["startTime"] = new { hours = 9, minutes = 0 },
        ["endTime"] = new { hours = 10, minutes = 0 },
        ["isBackground"] = false,
        ["activityId"] = activityId,
        ["importanceId"] = null,
        ["status"] = "NotStarted",
        ["calendarId"] = calendarId,
        ["date"] = null,
        ["todolistId"] = null,
        ["location"] = null,
        ["notes"] = null,
        ["actualStartTime"] = null
    };

    private Task<HttpResponseMessage> PostTaskAsync(Dictionary<string, object?> body) =>
        CreateUserRoleClient().PostAsJsonAsync("api/planner-task", body, JsonOpts, CancellationToken);

    private async Task<(long ActivityId, long CalendarId)> SeedActivityAndDayAsync(string name)
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, name, ct: CancellationToken);
        // 2029 is outside the window CalendarSeeder fills (this year + next), so this row is the test's own.
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2029, 3, 6), ct: CancellationToken);
        return (activityId, calendarId);
    }

    private async Task<(long ActivityId, long ImportanceId)> SeedActivityAndImportanceAsync(string name)
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, name, ct: CancellationToken);
        var importanceId = await PlanningTestSeedHelper.SeedTaskImportanceAsync(db, 42, ct: CancellationToken);
        return (activityId, importanceId);
    }
}
