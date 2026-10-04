using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.infrastructure.jobs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// <see cref="CompleteFinishedPlannerTasksJobHandler"/> — the sweep that closes a rest block booked with the
/// leisure picker's "do it now" once its slot is over. It writes status on its own schedule with nobody watching,
/// so a mistake here is silent: these assert on rows, per the backend's "silent failures" rule.
/// <para>
/// Dated relative to the fixture user's day (the fixture user is on UTC): yesterday's slot has certainly ended
/// and tomorrow's certainly has not, whatever hour the suite runs at.
/// </para>
/// </summary>
[Collection("Postgres")]
public class CompleteFinishedPlannerTasksJobTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact(DisplayName = "An auto-complete block that is in progress and over is completed, with its actual end on the slot's end")]
    public async Task InProgressAndOver_IsCompleted()
    {
        var taskId = await SeedAsync(Today.AddDays(-1), PlannerTaskStatus.InProgress, autoComplete: true, actualStart: new TimeOnly(16, 13));

        await RunJobAsync();

        var task = await LoadAsync(taskId);
        task.Status.Should().Be(PlannerTaskStatus.Completed);
        task.ActualStartTime.Should().Be(new TimeOnly(16, 13), "when the user actually began is kept");
        task.ActualEndTime.Should().Be(new TimeOnly(17, 0), "rest that was never stopped by hand ran to the end of its slot");
    }

    [Fact(DisplayName = "An auto-complete block whose slot has not ended yet is left in progress")]
    public async Task InProgressNotOverYet_IsLeftAlone()
    {
        var taskId = await SeedAsync(Today.AddDays(1), PlannerTaskStatus.InProgress, autoComplete: true);

        await RunJobAsync();

        (await LoadAsync(taskId)).Status.Should().Be(PlannerTaskStatus.InProgress);
    }

    [Fact(DisplayName = "An auto-complete block that never started is not completed — nothing said it happened")]
    public async Task NotStarted_IsLeftAlone()
    {
        var taskId = await SeedAsync(Today.AddDays(-1), PlannerTaskStatus.NotStarted, autoComplete: true);

        await RunJobAsync();

        (await LoadAsync(taskId)).Status.Should().Be(PlannerTaskStatus.NotStarted);
    }

    [Fact(DisplayName = "An ordinary in-progress task is never auto-completed, however long ago it ended")]
    public async Task WithoutTheFlag_IsLeftAlone()
    {
        var taskId = await SeedAsync(Today.AddDays(-1), PlannerTaskStatus.InProgress, autoComplete: false);

        await RunJobAsync();

        (await LoadAsync(taskId)).Status.Should().Be(PlannerTaskStatus.InProgress);
    }

    // ---- helpers ---------------------------------------------------------

    private async Task<long> SeedAsync(DateOnly date, PlannerTaskStatus status, bool autoComplete, TimeOnly? actualStart = null)
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"rest-{Guid.NewGuid():N}", UserId, CancellationToken);
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, date, UserId, CancellationToken);
        var taskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(
            db, activityId, calendarId, new TimeOnly(16, 15), new TimeOnly(17, 0), UserId, status, ct: CancellationToken);

        var task = await db.Set<PlannerTask>().SingleAsync(t => t.Id == taskId, CancellationToken);
        task.AutoCompleteAtEnd = autoComplete;
        task.ActualStartTime = actualStart;
        await db.SaveChangesAsync(CancellationToken);
        return taskId;
    }

    private async Task<PlannerTask> LoadAsync(long taskId)
    {
        await using var db = CreateDbContext();
        return await db.Set<PlannerTask>().AsNoTracking().SingleAsync(t => t.Id == taskId, CancellationToken);
    }

    /// <summary>The way the Scheduler's dispatcher runs it: one DI scope per fire, a hand-built context.</summary>
    private async Task RunJobAsync()
    {
        await using var scope = Fixture.AdminAndUserFactory.Services.CreateAsyncScope();
        var handler = ActivatorUtilities.CreateInstance<CompleteFinishedPlannerTasksJobHandler>(scope.ServiceProvider);

        var now = DateTime.UtcNow;
        await handler.ExecuteAsync(new ScheduledJobContext
        {
            ScheduledFireTimeUtc = now,
            ActualFireTimeUtc = now,
            JobKey = handler.Key,
            CorrelationId = "complete-finished-planner-tasks-test",
            TriggerSource = TriggerSource.Manual
        }, CancellationToken);
    }
}
