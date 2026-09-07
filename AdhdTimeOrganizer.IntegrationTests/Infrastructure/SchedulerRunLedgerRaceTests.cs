using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Quartz;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.Testing;
using Sydowwe.Scheduler.application.job;
using Sydowwe.Scheduler.domain.entity;
using Sydowwe.Scheduler.domain.@enum;
using Sydowwe.Scheduler.infrastructure;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Infrastructure;

/// <summary>
/// The dispatcher's central invariant: <b>a handler that ran always leaves an audited ledger row.</b>
/// <para>
/// <c>ScheduledJobDispatcher</c> states it explicitly — the terminal run row is persisted in "its OWN save
/// first … so a successfully-run handler always leaves an audited ledger row, even if the registry
/// observability update below loses an optimistic-concurrency race". Everything downstream trusts that: the
/// run log is documented as the authoritative record, the dashboard reads it, the overdue sweep reads it, and
/// the alerting path deliberately throttles notifications *because* the ledger is the complete truth.
/// </para>
/// <para>
/// The invariant is silent when it breaks. No row appears, nothing is logged at the call site, and the job
/// simply looks like it never fired — which is indistinguishable from a job that genuinely did not fire. That
/// is CLAUDE.md's "assert on rows, not on routes" case exactly, and nothing in the suite covered it: the only
/// other test touching <c>RunningSince</c> (<see cref="SchedulerAttentionDetailTests"/>) sets the column by
/// hand and never drives a fire.
/// </para>
/// </summary>
[Collection("Postgres")]
public class SchedulerRunLedgerRaceTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string JobKey = "test.ledger-race";
    private const string HandlerKey = "test.ledger-race.handler";

    private long _jobId;

    protected override async Task SeedAsync(DbContext db)
    {
        var job = new ScheduledJob
        {
            JobKey = JobKey,
            HandlerKey = HandlerKey,
            OwnerModule = "IntegrationTests",
            ScheduleType = JobScheduleType.Cron,
            Cron = "0 0 3 * * ?",
            MisfirePolicy = MisfirePolicy.FireAndProceed,
            // The gate is what gives the test its seam: it is consulted after the dispatcher has loaded and
            // tracked the registry row, but before the in-flight marker is saved.
            DisallowConcurrent = true,
            // No retry and no alert: this fire succeeds, and neither path should colour the assertions.
            MaxRetries = 0,
            AlertOnFailure = false,
            Status = JobStatus.Active
        };
        db.Set<ScheduledJob>().Add(job);
        await db.SaveChangesAsync(CancellationToken);

        _jobId = job.Id;
    }

    /// <summary>
    /// A control op (pause / resume / re-register from the dashboard) commits against the registry row in the
    /// window between the dispatcher loading it and saving the in-flight marker — an ordinary single-node
    /// occurrence, not a multi-node one. The marker's own save is documented as best-effort and swallows the
    /// lost race, so the fire must carry on and the successful handler must still be audited.
    /// </summary>
    [Fact]
    public async Task AFireWhoseInFlightMarkerLosesItsRace_StillWritesTheRunRow()
    {
        var handler = new RecordingHandler();

        await using var db = CreateDbContext();

        // Bumps row_version on the registry row from a SEPARATE context, so the dispatcher's tracked copy is
        // left holding a stale concurrency token exactly the way a real control op leaves it.
        var gate = new BumpingConcurrencyGate(async () =>
        {
            await using var other = CreateDbContext();
            var job = await other.Set<ScheduledJob>().SingleAsync(j => j.Id == _jobId, CancellationToken);
            job.Description = "touched by a concurrent control op";
            await other.SaveChangesAsync(CancellationToken);
        });

        var dispatcher = new ScheduledJobDispatcher(
            db,
            [handler],
            gate,
            new NoOpRetryScheduler(),
            new NoOpFailureNotifier(),
            new NeverAlertThrottle(),
            NullLogger<ScheduledJobDispatcher>.Instance);

        var act = async () => await dispatcher.Execute(FakeJobExecutionContext.ManualFire(JobKey, CancellationToken));

        await act.Should().NotThrowAsync(
            "a Quartz fire must never bubble — the dispatcher's whole contract is failure isolation");

        gate.Fired.Should().BeTrue("the test is meaningless unless the bump actually landed mid-fire");
        handler.Ran.Should().BeTrue();

        await using var verify = CreateDbContext();
        var runs = await verify.Set<ScheduledJobRun>()
            .Where(r => r.ScheduledJobId == _jobId)
            .ToListAsync(CancellationToken);

        runs.Should().ContainSingle(
                "the handler ran to completion, so the append-only ledger owes exactly one terminal row")
            .Which.Outcome.Should().Be(RunOutcome.Succeeded);
    }

    // ─── fakes ───────────────────────────────────────────────────────────────

    private sealed class RecordingHandler : IScheduledJobHandler
    {
        public bool Ran { get; private set; }

        public string Key => HandlerKey;

        public Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct)
        {
            Ran = true;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Grants the gate, but runs <paramref name="bump"/> first — the one seam that sits between the registry
    /// load and the in-flight marker's save without reaching into the dispatcher.
    /// </summary>
    private sealed class BumpingConcurrencyGate(Func<Task> bump) : IJobConcurrencyGate
    {
        public bool Fired { get; private set; }

        public bool TryAcquire(string jobKey)
        {
            bump().GetAwaiter().GetResult();
            Fired = true;
            return true;
        }

        public void Release(string jobKey) { }
    }

    private sealed class NoOpRetryScheduler : IJobRetryScheduler
    {
        public Task ScheduleAsync(RetryFireRequest request, CancellationToken ct) => Task.CompletedTask;
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
    /// The five members <c>ScheduledJobDispatcher</c> actually reads off a fire. Everything else on Quartz's
    /// context throws, so a future dispatcher change that starts depending on one fails loudly here rather
    /// than quietly reading a default.
    /// </summary>
    private sealed class FakeJobExecutionContext : IJobExecutionContext
    {
        private FakeJobExecutionContext(JobDataMap dataMap, CancellationToken ct)
        {
            MergedJobDataMap = dataMap;
            CancellationToken = ct;
        }

        /// <summary>
        /// A manual (trigger-now) fire: off-schedule, so <c>ScheduledFireTime</c> is null and the occurrence
        /// dedup never applies. That keeps this test on the ledger invariant alone.
        /// </summary>
        public static FakeJobExecutionContext ManualFire(string jobKey, CancellationToken ct) =>
            new(new JobDataMap
            {
                { SchedulerQuartzConfig.JobKeyDataKey, jobKey },
                { SchedulerQuartzConfig.TriggerSourceDataKey, nameof(TriggerSource.Manual) }
            }, ct);

        public JobDataMap MergedJobDataMap { get; }
        public CancellationToken CancellationToken { get; }
        public DateTimeOffset FireTimeUtc { get; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? ScheduledFireTimeUtc => null;
        public object? Result { get; set; }

        // Qualified: Sydowwe.Framework.Contracts.scheduling has its own IScheduler, and both usings are needed here.
        public Quartz.IScheduler Scheduler => throw new NotSupportedException();
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
