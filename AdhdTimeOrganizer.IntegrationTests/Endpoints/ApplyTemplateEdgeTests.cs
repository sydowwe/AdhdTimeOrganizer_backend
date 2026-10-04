using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.application.service.reminder;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;
using Sydowwe.Framework.Testing;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Reminders.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-7 — the edges of <c>ApplyTemplatePlannerTaskEndpoint</c> that its conflict-resolution suite
/// (<see cref="ApplyTemplatePlannerTaskConflictResolutionTests"/>) deliberately leaves alone: how it names its
/// day, who it will name it for, what it stamps, how it reports a lost update, what the validator refuses, and
/// the one mode that must <b>not</b> cancel a reminder.
/// <para>
/// Nearly every failure mode here answers 201 or 404 rather than throwing, so every test re-reads rows — the
/// day, the template's usage counters and the module-side <c>ReminderDefinition</c> — rather than trusting the
/// status code alone.
/// </para>
/// </summary>
[Collection("Postgres")]
public class ApplyTemplateEdgeTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    // ─── A: by id vs by date, on an unplanned day ────────────────────────────

    /// <summary>
    /// The asymmetry `domain-map.md` § *Lazy day creation* states, pinned from both sides in one place: a day
    /// named by <b>date</b> is created if the user has never opened it, but a <b>calendar id</b> that resolves
    /// to nothing is a stale client rather than an unplanned day and still 404s. Reading only the by-date half
    /// (which <c>LazyCalendarCreationTests.ApplyingATemplateByDate_CreatesTheDay</c> already covers) would make
    /// "provision whatever the client names" look like the rule.
    /// </summary>
    [Fact(DisplayName = "By date an unplanned day is created; by id an unknown day is still a 404")]
    public async Task ApplyingByDate_CreatesTheDay_ButApplyingByAnUnknownId_Does404()
    {
        var (activityId, template) = await SeedApplyFixtureAsync("by-date-vs-id");
        var date = new DateOnly(2034, 6, 11); // far outside the seeded years

        await AssertNoCalendarAsync(date);
        var calendarsBefore = await CountCalendarsAsync();

        var byDate = await CreateUserRoleClient().PostAsJsonAsync("api/calendar/apply-planner-template", new
        {
            TemplateId = template.Id,
            Date = date,
            ConflictResolution = "Ignore",
            TasksFromTemplate = new[] { NewTask(activityId, 9, 0, 10, 0) }
        }, JsonOpts, CancellationToken);

        byDate.StatusCode.Should().Be(HttpStatusCode.Created);
        (await FindCalendarAsync(date)).Should().NotBeNull("naming a day by date provisions it");
        (await CountCalendarsAsync()).Should().Be(calendarsBefore + 1);

        var calendarsAfterByDate = await CountCalendarsAsync();

        var byId = await ApplyAsync(long.MaxValue, template.Id, "Ignore", [NewTask(activityId, 11, 0, 12, 0)]);

        byId.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "an id resolving to nothing is a stale client, not a day to provision");
        (await CountCalendarsAsync()).Should().Be(calendarsAfterByDate, "the 404 path must not leave a day behind");
    }

    // ─── B: cross-user calendar ──────────────────────────────────────────────

    /// <summary>
    /// Another user's day does not resolve through the global query filter, so the single-day endpoint answers
    /// a flat <b>404</b> — while the batched sibling answers <c>succeeded: false</c> for that same day
    /// (<see cref="CalendarBatchEndpointTests.ApplyTemplateBatch_AppliesPerDay_AndFailsOnlyTheUnreachableDay"/>).
    /// Both shapes are deliberate: one day named, one answer; N days named, one result each. Sending the same
    /// unreachable id down both routes in one test is what makes the pair legible instead of looking like a
    /// disagreement.
    /// </summary>
    [Fact(DisplayName = "Another user's calendar 404s here and merely fails its own row in the batch")]
    public async Task ApplyingToAnotherUsersCalendar_Is404_AndLeavesThatDayUntouched()
    {
        var (activityId, template) = await SeedApplyFixtureAsync("cross-user-calendar");

        long theirCalendarId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            theirCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(
                db, new DateOnly(2029, 7, 1), ReminderSeedHelper.OtherUserId, CancellationToken);
        }

        var response = await ApplyAsync(theirCalendarId, template.Id, "Ignore", [NewTask(activityId, 9, 0, 10, 0)]);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using (var db = CreateDbContext())
        {
            var theirCalendar = await db.Set<Calendar>().IgnoreQueryFilters()
                .SingleAsync(c => c.Id == theirCalendarId, CancellationToken);
            theirCalendar.AppliedTemplateId.Should().BeNull("their day was never stamped");
            theirCalendar.AppliedTemplateName.Should().BeNull();

            (await db.Set<PlannerTask>().IgnoreQueryFilters().AnyAsync(t => t.CalendarId == theirCalendarId, CancellationToken))
                .Should().BeFalse("no task may be filed on a day the caller cannot see");

            var stored = await db.Set<TaskPlannerDayTemplate>().IgnoreQueryFilters()
                .SingleAsync(t => t.Id == template.Id, CancellationToken);
            stored.UsageCount.Should().Be(0, "the stamp happens after the day resolves, so a 404 must not count as a use");
            stored.LastUsedAt.Should().BeNull();
        }

        // The contrast: the same unreachable id, down the batched route, is one failed row and a 200.
        var batch = await CreateUserRoleClient().PostAsJsonAsync("api/calendar/apply-planner-template/batch", new
        {
            TemplateId = template.Id,
            CalendarIds = new[] { theirCalendarId },
            ConflictResolution = "Ignore",
            TasksFromTemplate = new[] { NewTask(activityId, 9, 0, 10, 0) }
        }, JsonOpts, CancellationToken);

        batch.StatusCode.Should().Be(HttpStatusCode.OK);
        var batchBody = await batch.Content.ReadFromJsonAsync<CalendarBatchResultResponse>(JsonOpts, CancellationToken);
        batchBody!.Results.Should().Equal([CalendarBatchResult.Failed(theirCalendarId, CalendarBatchFailureReason.NotFound)],
            "the batch reports the unreachable day per row rather than failing the whole request");
    }

    // ─── C: cross-user template ──────────────────────────────────────────────

    /// <summary>
    /// The template is looked up under the same filter as the day, so someone else's template is a 404 even
    /// when the caller owns the day perfectly well. The counters are the assertion that matters: a template
    /// resolved but rejected later would still have been stamped, and <c>UsageCount</c> is the only place that
    /// would show it.
    /// </summary>
    [Fact(DisplayName = "Another user's template is a 404, and their usage counters do not move")]
    public async Task ApplyingAnotherUsersTemplate_Is404_AndWritesNothing()
    {
        var (activityId, _) = await SeedApplyFixtureAsync("cross-user-template");
        var calendarId = await SeedCalendarAsync(new DateOnly(2029, 7, 2));

        long theirTemplateId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            theirTemplateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(
                db, ReminderSeedHelper.OtherUserId, "Their template", CancellationToken);
        }

        var response = await ApplyAsync(calendarId, theirTemplateId, "Ignore", [NewTask(activityId, 9, 0, 10, 0)]);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using (var db = CreateDbContext())
        {
            var theirTemplate = await db.Set<TaskPlannerDayTemplate>().IgnoreQueryFilters()
                .SingleAsync(t => t.Id == theirTemplateId, CancellationToken);
            theirTemplate.UsageCount.Should().Be(0);
            theirTemplate.LastUsedAt.Should().BeNull();

            var myCalendar = await db.Set<Calendar>().IgnoreQueryFilters().SingleAsync(c => c.Id == calendarId, CancellationToken);
            myCalendar.AppliedTemplateId.Should().BeNull("my own day must not be stamped with a template that did not resolve");

            (await db.Set<PlannerTask>().IgnoreQueryFilters().AnyAsync(t => t.CalendarId == calendarId, CancellationToken))
                .Should().BeFalse();
        }
    }

    // ─── D: the stamp ────────────────────────────────────────────────────────

    /// <summary>
    /// All five effects of <c>PlannerTemplateApplier.StampTemplate</c> at once, in both of its shapes. The
    /// second row is the one the conflict-resolution suite never exercises: <c>?? calendar.WakeUpTime</c> means
    /// a template without a sleep window leaves the day's own window <b>alone</b> — it does not blank it. A
    /// regression there writes 00:00 over a real wake-up time and nothing fails.
    /// </summary>
    [Theory(DisplayName = "The stamp takes the template's sleep window when it has one, and leaves the day's own when it does not")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApplyingATemplate_StampsTheDay_AndOnlyOverwritesTheSleepWindowItHas(bool templateHasSleepWindow)
    {
        var (activityId, template) = await SeedApplyFixtureAsync(
            $"stamp-{templateHasSleepWindow}",
            wakeUpTime: templateHasSleepWindow ? new TimeOnly(6, 45) : null,
            bedTime: templateHasSleepWindow ? new TimeOnly(22, 30) : null);

        // PlanningTestSeedHelper always seeds 07:00 / 23:00, which is what must survive the null case.
        var calendarId = await SeedCalendarAsync(new DateOnly(2029, 7, 3));

        var response = await ApplyAsync(calendarId, template.Id, "Ignore", [NewTask(activityId, 9, 0, 10, 0)]);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = CreateDbContext();

        var calendar = await db.Set<Calendar>().SingleAsync(c => c.Id == calendarId, CancellationToken);
        calendar.AppliedTemplateId.Should().Be(template.Id);
        calendar.AppliedTemplateName.Should().Be(template.Name);
        calendar.WakeUpTime.Should().Be(templateHasSleepWindow ? new TimeOnly(6, 45) : new TimeOnly(7, 0));
        calendar.BedTime.Should().Be(templateHasSleepWindow ? new TimeOnly(22, 30) : new TimeOnly(23, 0));

        var stored = await db.Set<TaskPlannerDayTemplate>().SingleAsync(t => t.Id == template.Id, CancellationToken);
        stored.UsageCount.Should().Be(1);
        stored.LastUsedAt.Should().NotBeNull();
        stored.LastUsedAt!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    // ─── E: CQ-9 — error mapping ─────────────────────────────────────────────

    /// <summary>
    /// <c>CQ-9</c>. The stamp writes to the calendar, which carries a live <c>row_version</c> from
    /// <c>BaseEntityConfigure()</c>, so two devices applying templates to the same day genuinely race and the
    /// loser throws <see cref="DbUpdateConcurrencyException"/>. The endpoint's catch block computes
    /// <c>DbUtils.HandleException(ex, "Create").ErrorType</c> and used to discard it in favour of a literal
    /// <c>Send.ErrorsAsync(400, ct)</c> — reporting a transient conflict as a client input error, so a client
    /// that retries on 409 instead showed "invalid request" and stopped. It now maps through
    /// <c>EndpointHelper.ToStatusCode</c>, the same pair <c>PatchPlannerTaskStatusEndpoint</c> uses, and the
    /// <c>Summary</c> block declares 409 alongside it.
    /// <para>
    /// Forced at the DbContext level rather than over HTTP so there is no scheduler race to flake on; the
    /// weak-but-real HTTP half is <see cref="TwoConcurrentApplies_NeverAnswer400"/>.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "CQ-9: a lost update on the day's row_version maps to 409, not 400")]
    public async Task LostUpdateOnTheCalendar_MapsToA409_NotA400()
    {
        var calendarId = await SeedCalendarAsync(new DateOnly(2029, 7, 4));

        await using var contextA = CreateDbContext();
        var trackedByA = await contextA.Set<Calendar>().FirstAsync(c => c.Id == calendarId, CancellationToken);

        await using (var contextB = CreateDbContext())
        {
            var trackedByB = await contextB.Set<Calendar>().FirstAsync(c => c.Id == calendarId, CancellationToken);
            trackedByB.AppliedTemplateName = "Applied by the other device";
            await contextB.SaveChangesAsync(CancellationToken); // row_version moves
        }

        trackedByA.AppliedTemplateName = "Applied by this device";
        var act = async () => await contextA.SaveChangesAsync(CancellationToken);

        var thrown = (await act.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "BaseEntityConfigure() puts a live row_version token on the calendar")).Which;

        var result = DbUtils.HandleException(thrown, "Create");
        EndpointHelper.ToStatusCode(result.ErrorType).Should().Be(409);

        await using var verifyDb = CreateDbContext();
        var row = await verifyDb.Set<Calendar>().AsNoTracking().SingleAsync(c => c.Id == calendarId, CancellationToken);
        row.AppliedTemplateName.Should().Be("Applied by the other device", "the loser's write was refused, not silently applied");
    }

    /// <summary>
    /// The HTTP half of <c>CQ-9</c>, with the weak assertion the race allows: whether the two requests actually
    /// overlap is up to the scheduler, so this pins what must hold either way — one applier wins, and a loser
    /// can only ever have lost the <c>row_version</c> race, which is a 409 and never a 400.
    /// </summary>
    [Fact(DisplayName = "RACE (weak assertion): two overlapping applies to one day never answer 400")]
    public async Task TwoConcurrentApplies_NeverAnswer400()
    {
        var (activityId, template) = await SeedApplyFixtureAsync("concurrent-apply");
        var calendarId = await SeedCalendarAsync(new DateOnly(2029, 7, 5));

        var responses = await Task.WhenAll(
            ApplyAsync(calendarId, template.Id, "Ignore", [NewTask(activityId, 9, 0, 10, 0)]),
            ApplyAsync(calendarId, template.Id, "Ignore", [NewTask(activityId, 11, 0, 12, 0)]));

        responses.Should().Contain(r => r.StatusCode == HttpStatusCode.Created, "at least one of the two writers must win");
        responses.Where(r => r.StatusCode != HttpStatusCode.Created)
            .Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict,
                "a lost update is the only way the loser can fail here, and that is a 409");
    }

    // ─── F: validator rules ──────────────────────────────────────────────────

    /// <summary>
    /// <c>ApplyTemplateToTaskPlannerValidator</c>, one row per rule. The day is named exactly once (the same XOR
    /// <c>PlannerTaskRequest</c> carries — both can disagree, and the loser is a template quietly applied to a
    /// day nobody asked for), there is at least one line to apply, and each line is a forward span on a real
    /// activity.
    /// <para>
    /// The ordering rule is worth naming: this validator has it and <c>PlannerTaskValidator</c>'s siblings do
    /// not, so this row is the reference behaviour the other three should match (<c>DOC-1</c>).
    /// </para>
    /// </summary>
    [Theory(DisplayName = "The validator refuses an ambiguously named day, an empty template and a malformed line")]
    [InlineData("both a calendar id and a date")]
    [InlineData("neither a calendar id nor a date")]
    [InlineData("a null task list")]
    [InlineData("an empty task list")]
    [InlineData("a line ending before it starts")]
    [InlineData("a line starting and ending at the same minute")]
    [InlineData("a line with no activity")]
    [InlineData("a conflict resolution outside the enum")]
    public async Task ApplyingAMalformedRequest_Is400(string shape)
    {
        var (activityId, template) = await SeedApplyFixtureAsync($"validator-{shape}");
        var calendarId = await SeedCalendarAsync(new DateOnly(2029, 7, 6));

        object? calendarIdField = calendarId;
        object? dateField = null;
        object? conflictResolution = "Ignore";
        object?[]? tasks = [NewTask(activityId, 9, 0, 10, 0)];

        switch (shape)
        {
            case "both a calendar id and a date":
                dateField = new DateOnly(2029, 7, 7);
                break;
            case "neither a calendar id nor a date":
                calendarIdField = null;
                break;
            case "a null task list":
                tasks = null;
                break;
            case "an empty task list":
                tasks = [];
                break;
            case "a line ending before it starts":
                tasks = [NewTask(activityId, 12, 0, 11, 0)];
                break;
            case "a line starting and ending at the same minute":
                tasks = [NewTask(activityId, 10, 0, 10, 0)];
                break;
            case "a line with no activity":
                tasks = [NewTask(0, 9, 0, 10, 0)];
                break;
            case "a conflict resolution outside the enum":
                conflictResolution = "Sideways";
                break;
        }

        var response = await CreateUserRoleClient().PostAsJsonAsync("api/calendar/apply-planner-template", new
        {
            TemplateId = template.Id,
            CalendarId = calendarIdField,
            Date = dateField,
            ConflictResolution = conflictResolution,
            TasksFromTemplate = tasks
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the request is rejected at the door: {0}", shape);

        await using var db = CreateDbContext();
        (await db.Set<PlannerTask>().IgnoreQueryFilters().AnyAsync(t => t.CalendarId == calendarId, CancellationToken))
            .Should().BeFalse("a rejected request writes nothing");
        var stored = await db.Set<TaskPlannerDayTemplate>().SingleAsync(t => t.Id == template.Id, CancellationToken);
        stored.UsageCount.Should().Be(0);
    }

    // ─── G: reminders on tasks Ignore does not delete ────────────────────────

    /// <summary>
    /// The negative control the conflict-resolution suite lacks. <c>Overwrite</c> and both merge modes delete
    /// existing tasks and must cancel the module-side <c>ReminderDefinition</c> of each — asserted there via
    /// <c>AssertReminderCancelledAsync</c>. <c>Ignore</c> deletes nothing, so a reminder on a task the new lines
    /// merely overlap must still be <b>registered and live</b> afterwards.
    /// <para>
    /// Worth pinning on its own row because <c>StagedTaskDeletions</c> reads the ChangeTracker rather than a
    /// list threaded through the four branches: anything that started staging deletes under <c>Ignore</c> would
    /// cancel these reminders silently, and the endpoint would still answer 201 with the task still present in
    /// the response.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "Ignore deletes no existing task, so an attached reminder stays registered")]
    public async Task Ignore_LeavesTheRemindersOfOverlappedExistingTasksRegistered()
    {
        var (activityId, template) = await SeedApplyFixtureAsync("ignore-reminders");
        var calendarId = await SeedCalendarAsync(new DateOnly(2029, 7, 8));

        long overlappedId;
        await using (var db = CreateDbContext())
            overlappedId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(
                db, activityId, calendarId, new TimeOnly(10, 0), new TimeOnly(11, 0), ct: CancellationToken);

        var reminderId = await CreateTaskReminderAsync(overlappedId);

        var response = await ApplyAsync(calendarId, template.Id, "Ignore",
        [
            NewTask(activityId, 10, 30, 10, 45) // fully inside the existing task -> the NEW task is what gets dropped
        ]);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using (var db = CreateDbContext())
        {
            (await db.Set<PlannerTask>().IgnoreQueryFilters().AnyAsync(t => t.Id == overlappedId, CancellationToken))
                .Should().BeTrue("Ignore drops the conflicting new task, never the existing one");
            (await db.Set<PlannerTask>().IgnoreQueryFilters().CountAsync(t => t.CalendarId == calendarId, CancellationToken))
                .Should().Be(1, "the only new line conflicted, so nothing was added either");
        }

        await using var verifyDb = CreateDbContext();
        var definition = await verifyDb.Set<ReminderDefinition>()
            .Where(d => d.OwnerModule == ReminderRegistrationService.OwnerModule
                        && d.SubjectType == ReminderRegistrationService.SubjectType
                        && d.SubjectId == reminderId.ToString()
                        && d.Kind == ReminderRegistrationService.Kind)
            .SingleAsync(CancellationToken);

        definition.Status.Should().Be(ReminderStatus.Active, "the task it belongs to was never deleted");
        definition.NextOccurrenceAt.Should().NotBeNull();
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private async Task<(long ActivityId, TaskPlannerDayTemplate Template)> SeedApplyFixtureAsync(
        string tag, TimeOnly? wakeUpTime = null, TimeOnly? bedTime = null)
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"edge-{tag}", ct: CancellationToken);

        var template = new TaskPlannerDayTemplate
        {
            UserId = UserId,
            Name = $"Edge template {tag}",
            IsActive = true,
            SuggestedForDayType = DayType.Workday,
            DefaultWakeUpTime = wakeUpTime,
            DefaultBedTime = bedTime
        };
        db.Set<TaskPlannerDayTemplate>().Add(template);
        await db.SaveChangesAsync(CancellationToken);

        return (activityId, template);
    }

    private async Task<long> SeedCalendarAsync(DateOnly date)
    {
        await using var db = CreateDbContext();
        return await PlanningTestSeedHelper.SeedCalendarAsync(db, date, ct: CancellationToken);
    }

    private static object NewTask(long activityId, int startH, int startM, int endH, int endM) => new
    {
        StartTime = new { Hours = startH, Minutes = startM },
        EndTime = new { Hours = endH, Minutes = endM },
        IsBackground = false,
        ActivityId = activityId,
        Status = "NotStarted"
    };

    private Task<HttpResponseMessage> ApplyAsync(long calendarId, long templateId, string conflictResolution, object[] tasksFromTemplate) =>
        CreateUserRoleClient().PostAsJsonAsync("api/calendar/apply-planner-template", new
        {
            CalendarId = calendarId,
            TemplateId = templateId,
            ConflictResolution = conflictResolution,
            TasksFromTemplate = tasksFromTemplate
        }, JsonOpts, CancellationToken);

    private async Task<long> CreateTaskReminderAsync(long plannerTaskId)
    {
        var response = await CreateClient().PostAsJsonAsync("api/reminder", new
        {
            Title = "Reminder that must survive",
            RemindAt = DateTime.UtcNow.AddHours(2),
            LeadOffsetsMinutes = new[] { 0 },
            PlannerTaskId = plannerTaskId
        }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<long>(CancellationToken);
    }

    private async Task<Calendar?> FindCalendarAsync(DateOnly date)
    {
        await using var db = CreateDbContext();
        return await db.Set<Calendar>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.UserId == UserId && c.Date == date, CancellationToken);
    }

    private async Task AssertNoCalendarAsync(DateOnly date) =>
        (await FindCalendarAsync(date)).Should().BeNull("the test is only meaningful on a date with no row");

    private async Task<int> CountCalendarsAsync()
    {
        await using var db = CreateDbContext();
        return await db.Set<Calendar>().IgnoreQueryFilters().CountAsync(c => c.UserId == UserId, CancellationToken);
    }
}
