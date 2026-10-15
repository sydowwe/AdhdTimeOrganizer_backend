using AdhdTimeOrganizer.History.application.job;
using AdhdTimeOrganizer.History.domain.model.entity.timerAlarm;
using AdhdTimeOrganizer.History.domain.model.@enum;
using AdhdTimeOrganizer.History.domain.serviceContract;
using AdhdTimeOrganizer.History.infrastructure.scheduling;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.infrastructure.email;
using Sydowwe.Scheduler.domain.entity;
using Sydowwe.Scheduler.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The boot registrar's stranded-job sweep, asserted on the registry rows.
/// <para>
/// An alarm row that vanishes without a cancel — it cascades away with its user — leaves a Scheduler job no one
/// re-registers: on the in-memory job store it has no trigger, so it sits <c>Active</c> and overdue for good
/// (scheduler findings B6). Nothing throws and nothing logs; the jobs list just fills with alarms that never
/// ring. The sweep must retire those, and must not touch the alarms that are still pending.
/// </para>
/// </summary>
[Collection("Postgres")]
public class TimerBoundaryAlarmRegistrarTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly KeyValuePair<string, string?>[] EmailOff =
        [new($"{EmailNotificationOptions.SectionName}:Enabled", "false")];

    [Fact(DisplayName = "Boot removes an alarm job whose row is gone, and keeps the pending one")]
    public async Task BootRemovesStrandedAlarmJobsAndKeepsLiveOnes()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);

        var liveKey = await ScheduleLiveAlarmAsync(factory);
        var strandedKey = await SeedStrandedJobAsync(createdAt: DateTime.UtcNow.AddDays(-14));

        await new TimerBoundaryAlarmRegistrar(factory.Services, NullLogger<TimerBoundaryAlarmRegistrar>.Instance)
            .StartAsync(CancellationToken);

        (await StatusOfAsync(strandedKey)).Should().Be(JobStatus.Removed, "its alarm row no longer exists");
        (await StatusOfAsync(liveKey)).Should().Be(JobStatus.Active, "its alarm is still pending");
    }

    [Fact(DisplayName = "A job registered after the owner read its rows is never treated as stranded")]
    public async Task AJobRegisteredDuringTheReadIsKept()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        // The host boots on first access, and its own registrar would sweep the row seeded below. Boot it first.
        await using var scope = factory.Services.CreateAsyncScope();

        var readStartedAt = DateTime.UtcNow.AddMinutes(-1);
        // Stands in for a timer started while the registrar was reading: its row was not in the read, so its
        // key is missing from the keep set — only the cutoff protects it.
        var lateKey = await SeedStrandedJobAsync(createdAt: DateTime.UtcNow);

        var removed = await scope.ServiceProvider.GetRequiredService<IScheduler>()
            .RemoveJobsNotInAsync(TimerBoundaryAlarmJobHandler.HandlerKey, [], readStartedAt, CancellationToken);

        (await StatusOfAsync(lateKey)).Should().Be(JobStatus.Active);
        removed.Should().Be(0);
    }

    // ---- helpers -----------------------------------------------------------------------------------

    private async Task<string> ScheduleLiveAlarmAsync(ITestClientFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ITimerBoundaryAlarmService>();
        await service.ReplaceSessionAsync(FakeLoggedUserService.TestUserId, Guid.NewGuid(),
        [
            new TimerBoundaryAlarm
            {
                SessionId = Guid.Empty,
                BoundaryAt = DateTime.UtcNow.AddMinutes(25),
                Phase = TimerBoundaryPhase.Focus
            }
        ], CancellationToken);

        await using var db = CreateDbContext();
        return (await db.Set<TimerBoundaryAlarm>().SingleAsync(CancellationToken)).JobKey;
    }

    /// <summary>A registry row under the alarm handler with no alarm row behind it — the stranded shape.</summary>
    private async Task<string> SeedStrandedJobAsync(DateTime createdAt)
    {
        var runAt = DateTime.UtcNow.AddDays(-14);
        var job = new ScheduledJob
        {
            // An id no alarm row will ever have, so the key cannot collide with a live one.
            JobKey = TimerBoundaryAlarm.JobKeyFor(-Random.Shared.NextInt64(1, long.MaxValue)),
            HandlerKey = TimerBoundaryAlarmJobHandler.HandlerKey,
            OwnerModule = "History",
            ScheduleType = JobScheduleType.Once,
            RunAtUtc = runAt,
            NextRunAt = runAt,
            MisfirePolicy = MisfirePolicy.FireAndProceed,
            AlertOnFailure = false,
            Status = JobStatus.Active,
            CreatedTimestamp = createdAt
        };

        await using var db = CreateDbContext();
        db.Set<ScheduledJob>().Add(job);
        await db.SaveChangesAsync(CancellationToken);
        return job.JobKey;
    }

    private async Task<JobStatus> StatusOfAsync(string jobKey)
    {
        await using var db = CreateDbContext();
        return await db.Set<ScheduledJob>().Where(j => j.JobKey == jobKey).Select(j => j.Status).SingleAsync(CancellationToken);
    }
}
