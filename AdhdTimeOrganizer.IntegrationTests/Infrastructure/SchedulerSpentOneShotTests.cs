using System.Collections.Specialized;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Quartz;
using Quartz.Impl;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.Testing;
using Sydowwe.Scheduler.application.job;
using Sydowwe.Scheduler.domain.entity;
using Sydowwe.Scheduler.domain.@enum;
using Sydowwe.Scheduler.infrastructure;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Infrastructure;

/// <summary>
/// A one-shot whose occurrence is used up ends <c>Removed</c>, on the row.
/// <para>
/// Before this, a fired one-shot kept <c>Status = Active</c> with <c>NextRunAt = null</c> forever — nothing
/// threw, nothing logged, and the jobs list filled with alarms that looked live (scheduler findings B6/O2). The
/// one case that must <b>not</b> retire is a failure with a retry armed: the dispatcher skips a retry whose
/// job is no longer Active, so retiring early would cancel the retry it just queued.
/// </para>
/// </summary>
[Collection("Postgres")]
public class SchedulerSpentOneShotTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string HandlerKey = "test.spent-one-shot.handler";

    [Fact(DisplayName = "A one-shot that fired successfully is retired to Removed")]
    public async Task ASucceededOneShotIsRetired()
    {
        var (jobKey, runAt) = await SeedOneShotAsync(maxRetries: 0);

        await FireAsync(new OutcomeHandler(succeed: true), FakeContext.Scheduled(jobKey, runAt, CancellationToken));

        var job = await ReadJobAsync(jobKey);
        job.Status.Should().Be(JobStatus.Removed);
        job.NextRunAt.Should().BeNull();
        job.LastOutcome.Should().Be(RunOutcome.Succeeded, "the observability update still lands before the retirement");
    }

    [Fact(DisplayName = "A one-shot whose only attempt failed is retired — there is no further fire coming")]
    public async Task AFinallyFailedOneShotIsRetired()
    {
        var (jobKey, runAt) = await SeedOneShotAsync(maxRetries: 0);

        await FireAsync(new OutcomeHandler(succeed: false), FakeContext.Scheduled(jobKey, runAt, CancellationToken));

        (await ReadJobAsync(jobKey)).Status.Should().Be(JobStatus.Removed);
    }

    [Fact(DisplayName = "A failed one-shot with a retry armed stays Active until the last attempt")]
    public async Task AOneShotWithARetryArmedStaysActiveUntilTheChainEnds()
    {
        var (jobKey, runAt) = await SeedOneShotAsync(maxRetries: 1);
        var retries = new RecordingRetryScheduler();

        await FireAsync(new OutcomeHandler(succeed: false), FakeContext.Scheduled(jobKey, runAt, CancellationToken), retries);

        retries.Requests.Should().ContainSingle("the test is meaningless unless a retry was actually armed");
        (await ReadJobAsync(jobKey)).Status.Should().Be(JobStatus.Active,
            "a retry fires only for an Active job — retiring now would cancel it");

        await FireAsync(new OutcomeHandler(succeed: false), FakeContext.Retry(jobKey, attempt: 1, CancellationToken), retries);

        (await ReadJobAsync(jobKey)).Status.Should().Be(JobStatus.Removed, "the final attempt ends the chain");
    }

    [Fact(DisplayName = "A re-registered one-shot that already ran is retired again on its duplicate fire")]
    public async Task ADuplicateFireOfASpentOneShotRetiresIt()
    {
        var (jobKey, runAt) = await SeedOneShotAsync(maxRetries: 0, alreadyRanAt: true);
        var handler = new OutcomeHandler(succeed: true);

        await FireAsync(handler, FakeContext.Scheduled(jobKey, runAt, CancellationToken));

        handler.Ran.Should().BeFalse("the occurrence already executed, so the dedup must skip it");
        (await ReadJobAsync(jobKey)).Status.Should().Be(JobStatus.Removed,
            "boot re-registration reset it to Active; the duplicate fire is the last chance to put it back");
    }

    [Fact(DisplayName = "A manual fire of a pending one-shot does not use up its scheduled occurrence")]
    public async Task AManualFireLeavesAPendingOneShotActive()
    {
        var (jobKey, _) = await SeedOneShotAsync(maxRetries: 0);

        await FireAsync(new OutcomeHandler(succeed: true), FakeContext.Manual(jobKey, CancellationToken));

        (await ReadJobAsync(jobKey)).Status.Should().Be(JobStatus.Active);
    }

    [Fact(DisplayName = "A recurring job is never retired by a fire")]
    public async Task ARecurringJobStaysActive()
    {
        var jobKey = await SeedAsync(job =>
        {
            job.ScheduleType = JobScheduleType.Cron;
            job.Cron = "0 0 3 * * ?";
        });

        await FireAsync(new OutcomeHandler(succeed: true), FakeContext.Scheduled(jobKey, DateTime.UtcNow, CancellationToken, oneShot: false));

        (await ReadJobAsync(jobKey)).Status.Should().Be(JobStatus.Active);
    }

    // ---- helpers -----------------------------------------------------------------------------------

    private async Task FireAsync(IScheduledJobHandler handler, FakeContext context, IJobRetryScheduler? retries = null)
    {
        await using var db = CreateDbContext();
        var dispatcher = new ScheduledJobDispatcher(
            db,
            [handler],
            new JobConcurrencyGate(),
            retries ?? new RecordingRetryScheduler(),
            new NoOpFailureNotifier(),
            new NeverAlertThrottle(),
            NullLogger<ScheduledJobDispatcher>.Instance);

        try
        {
            await dispatcher.Execute(context);
        }
        finally
        {
            await context.Scheduler.Shutdown(CancellationToken);
        }
    }

    private async Task<ScheduledJob> ReadJobAsync(string jobKey)
    {
        await using var db = CreateDbContext();
        return await db.Set<ScheduledJob>().AsNoTracking().SingleAsync(j => j.JobKey == jobKey, CancellationToken);
    }

    private async Task<(string JobKey, DateTime RunAt)> SeedOneShotAsync(int maxRetries, bool alreadyRanAt = false)
    {
        // Whole seconds, as Postgres stores microseconds and the dedup compares the instant for equality.
        var runAt = DateTime.UtcNow.AddMinutes(-1);
        runAt = new DateTime(runAt.Ticks - runAt.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

        var jobKey = await SeedAsync(job =>
        {
            job.ScheduleType = JobScheduleType.Once;
            job.RunAtUtc = runAt;
            job.NextRunAt = runAt;
            job.MaxRetries = maxRetries;
        });

        if (alreadyRanAt)
        {
            await using var db = CreateDbContext();
            var job = await db.Set<ScheduledJob>().SingleAsync(j => j.JobKey == jobKey, CancellationToken);
            db.Set<ScheduledJobRun>().Add(new ScheduledJobRun
            {
                ScheduledJobId = job.Id,
                JobKeySnapshot = jobKey,
                HandlerKeySnapshot = HandlerKey,
                ScheduledFireTime = runAt,
                StartedAt = runAt,
                FinishedAt = runAt,
                Outcome = RunOutcome.Succeeded,
                TriggerSource = TriggerSource.Scheduled,
                CorrelationId = Guid.NewGuid().ToString("N")
            });
            await db.SaveChangesAsync(CancellationToken);
        }

        return (jobKey, runAt);
    }

    private async Task<string> SeedAsync(Action<ScheduledJob> configure)
    {
        var job = new ScheduledJob
        {
            JobKey = $"tests.spent-one-shot.{Guid.NewGuid():N}",
            HandlerKey = HandlerKey,
            OwnerModule = "IntegrationTests",
            MisfirePolicy = MisfirePolicy.FireAndProceed,
            AlertOnFailure = false,
            Status = JobStatus.Active
        };
        configure(job);

        await using var db = CreateDbContext();
        db.Set<ScheduledJob>().Add(job);
        await db.SaveChangesAsync(CancellationToken);
        return job.JobKey;
    }

    // ─── fakes ───────────────────────────────────────────────────────────────

    private sealed class OutcomeHandler(bool succeed) : IScheduledJobHandler
    {
        public bool Ran { get; private set; }

        public string Key => HandlerKey;

        public Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct)
        {
            Ran = true;
            return succeed ? Task.CompletedTask : throw new InvalidOperationException("handler failed on purpose");
        }
    }

    private sealed class RecordingRetryScheduler : IJobRetryScheduler
    {
        public List<RetryFireRequest> Requests { get; } = [];

        public Task ScheduleAsync(RetryFireRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpFailureNotifier : IJobFailureNotifier
    {
        public Task NotifyJobFailedAsync(JobFailureAlert alert, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NeverAlertThrottle : IJobAlertThrottle
    {
        public bool ShouldAlert(string alertKey, DateTime nowUtc, TimeSpan? window = null) => false;
    }

    /// <summary>
    /// The members the dispatcher reads off a fire. <see cref="Scheduler"/> is a real, never-started RAM
    /// scheduler: a scheduled fire asks it for the trigger's next fire time, and one with no trigger answers
    /// null — exactly what a spent one-shot's lookup returns in production.
    /// </summary>
    private sealed class FakeContext : IJobExecutionContext
    {
        private FakeContext(JobDataMap dataMap, DateTime? scheduledFireTime, CancellationToken ct)
        {
            MergedJobDataMap = dataMap;
            ScheduledFireTimeUtc = scheduledFireTime is { } t ? new DateTimeOffset(t) : null;
            CancellationToken = ct;
            Scheduler = new StdSchedulerFactory(new NameValueCollection
            {
                ["quartz.scheduler.instanceName"] = $"spent-one-shot-{Guid.NewGuid():N}",
                ["quartz.threadPool.maxConcurrency"] = "1"
            }).GetScheduler(ct).GetAwaiter().GetResult();
        }

        public static FakeContext Scheduled(string jobKey, DateTime fireTime, CancellationToken ct, bool oneShot = true)
        {
            var map = new JobDataMap { { SchedulerQuartzConfig.JobKeyDataKey, jobKey } };
            if (oneShot)
                map.Put(SchedulerQuartzConfig.RunOnceAtDataKey, fireTime.Ticks);
            return new FakeContext(map, fireTime, ct);
        }

        public static FakeContext Retry(string jobKey, int attempt, CancellationToken ct) =>
            new(new JobDataMap
            {
                { SchedulerQuartzConfig.JobKeyDataKey, jobKey },
                { SchedulerQuartzConfig.TriggerSourceDataKey, nameof(TriggerSource.Retry) },
                { SchedulerQuartzConfig.RetryAttemptDataKey, attempt }
            }, null, ct);

        public static FakeContext Manual(string jobKey, CancellationToken ct) =>
            new(new JobDataMap
            {
                { SchedulerQuartzConfig.JobKeyDataKey, jobKey },
                { SchedulerQuartzConfig.TriggerSourceDataKey, nameof(TriggerSource.Manual) }
            }, null, ct);

        public JobDataMap MergedJobDataMap { get; }
        public CancellationToken CancellationToken { get; }
        public DateTimeOffset FireTimeUtc { get; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? ScheduledFireTimeUtc { get; }
        public object? Result { get; set; }

        // Qualified: Sydowwe.Framework.Contracts.scheduling has its own IScheduler.
        public Quartz.IScheduler Scheduler { get; }
        public ITrigger Trigger => throw new NotSupportedException();
        public ICalendar? Calendar => throw new NotSupportedException();
        public bool Recovering => throw new NotSupportedException();
        public TriggerKey RecoveringTriggerKey => throw new NotSupportedException();
        public int RefireCount => throw new NotSupportedException();
        public IJobDetail JobDetail => throw new NotSupportedException();
        public IJob JobInstance => throw new NotSupportedException();
        public DateTimeOffset? PreviousFireTimeUtc => throw new NotSupportedException();
        public DateTimeOffset? NextFireTimeUtc => throw new NotSupportedException();
        public string FireInstanceId => throw new NotSupportedException();
        public TimeSpan JobRunTime => throw new NotSupportedException();

        public void Put(object key, object objectValue) => throw new NotSupportedException();
        public object? Get(object key) => throw new NotSupportedException();
    }
}
