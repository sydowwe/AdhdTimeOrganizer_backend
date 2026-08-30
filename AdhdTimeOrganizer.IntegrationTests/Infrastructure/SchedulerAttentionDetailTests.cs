using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.Testing;
using Sydowwe.Scheduler.application.job;
using Sydowwe.Scheduler.domain.entity;
using Sydowwe.Scheduler.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Infrastructure;

/// <summary>
/// Pins the reason each row carries on the scheduler's three "needs attention" lists, and the replay
/// preconditions on the run-detail read.
/// <para>
/// A reason is close to the whole point of an attention list — without one the card degrades to a job key, an
/// owner module and a timestamp, and the operator has to open every row to learn anything. The failure mode is
/// silent in both directions: a missing <c>detail</c> deserializes to null and simply renders nothing, and a
/// <i>useless</i> one (restating the <c>nextRunAt</c> the client already draws) looks identical to a useful one
/// from any test that only checks the field is present. So these assert on what each sentence must actually
/// carry: the error off the run log, the in-flight fact, and the missing handler key.
/// </para>
/// <para>
/// The in-flight clause is the load-bearing half of the overdue reason. <c>RunningSince</c> is dispatcher
/// bookkeeping and is on no response DTO, so "stuck" versus "merely slow" — which is the difference between
/// paging someone and waiting — is a distinction no client can make for itself.
/// </para>
/// </summary>
[Collection("Postgres")]
public class SchedulerAttentionDetailTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string MissingHandlerKey = "Tests.HandlerThatWasRemoved";

    [Fact(DisplayName = "The failed list carries the error off the run that set LastOutcome")]
    public async Task FailedRowsCarryTheirError()
    {
        var (jobKey, id) = await SeedJobAsync(job =>
        {
            job.LastOutcome = RunOutcome.Failed;
            job.LastRunAt = DateTime.UtcNow.AddMinutes(-10);
        });

        await SeedRunAsync(id, jobKey, run =>
        {
            run.StartedAt = DateTime.UtcNow.AddMinutes(-10);
            run.Outcome = RunOutcome.Failed;
            run.ErrorType = "TimeoutException";
            run.ErrorMessage = "The handler did not complete within 30s.";
        });

        var detail = Detail(await FindAsync("failedJobs", jobKey));

        detail.Should().Contain("TimeoutException",
            "the error lives on the run log and the registry row has no column for it — this is the one thing the list adds");
        detail.Should().Contain("did not complete within 30s");
    }

    [Fact(DisplayName = "The failed list says so plainly when the failing run has aged out of retention")]
    public async Task FailedRowsWithNoSurvivingRunStillExplainThemselves()
    {
        var (jobKey, _) = await SeedJobAsync(job => job.LastOutcome = RunOutcome.Failed);

        Detail(await FindAsync("failedJobs", jobKey)).Should().NotBeNullOrWhiteSpace(
            "an empty reason on a listed row reads as a bug in the page rather than as a purged run");
    }

    [Fact(DisplayName = "An overdue row with a run in flight is reported as slow, not stuck")]
    public async Task OverdueRowDistinguishesSlowFromStuck()
    {
        var (jobKey, _) = await SeedJobAsync(job =>
        {
            job.NextRunAt = DateTime.UtcNow.AddHours(-5);
            job.RunningSince = DateTime.UtcNow.AddHours(-5);
        });

        var detail = Detail(await FindAsync("overdueJobs", jobKey));

        detail.Should().Contain("5 h", "the lateness is the first thing the operator reads");
        detail.Should().Contain("executing",
            "RunningSince is on no response DTO, so the client cannot tell a slow job from a missed fire");
    }

    [Fact(DisplayName = "An overdue row with nothing in flight says the fire was missed")]
    public async Task OverdueRowWithNothingRunningSaysTheFireWasMissed()
    {
        var (jobKey, _) = await SeedJobAsync(job =>
        {
            job.NextRunAt = DateTime.UtcNow.AddHours(-5);
            job.RunningSince = null;
        });

        Detail(await FindAsync("overdueJobs", jobKey)).Should().Contain("no run is in flight");
    }

    [Fact(DisplayName = "The orphaned list names the handler key, not the job key the row already shows")]
    public async Task OrphanedRowsNameTheMissingHandlerKey()
    {
        var (jobKey, _) = await SeedJobAsync(job => job.HandlerKey = MissingHandlerKey);

        Detail(await FindAsync("orphanedJobs", jobKey)).Should().Contain(MissingHandlerKey,
            "the row renders jobKey; it is handlerKey that stopped resolving, and only that names what to fix");
    }

    [Fact(DisplayName = "Run detail reports the state of the job behind the run, so replay can be gated before the click")]
    public async Task RunDetailCarriesTheReplayPreconditions()
    {
        var (jobKey, jobId) = await SeedJobAsync(job => job.HandlerKey = MissingHandlerKey);
        var runId = await SeedRunAsync(jobId, jobKey, run =>
        {
            run.StartedAt = DateTime.UtcNow.AddMinutes(-2);
            run.Outcome = RunOutcome.Succeeded;
        });

        var response = await CreateUserRoleClient().GetAsync($"api/scheduler-dashboard/run/{runId}", CancellationToken);
        response.EnsureSuccessStatusCode();
        var detail = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken);

        detail.GetProperty("isJobOrphaned").GetBoolean().Should().BeTrue(
            "ReplayJobRunEndpoint documents a 409 for exactly this — a known-certain failure is worth predicting");
        detail.GetProperty("jobStatus").GetString().Should().Be(nameof(JobStatus.Active),
            "status is the other half of the 409 condition and is reported as it is now, not as it was at run time");
    }

    // ---- helpers -----------------------------------------------------------------------------------

    private static string Detail(JsonElement row) => row.GetProperty("detail").GetString()!;

    /// <summary>Finds the seeded row on one of the health lists. The lists are registry-wide, so they carry the host's real jobs too.</summary>
    private async Task<JsonElement> FindAsync(string list, string jobKey)
    {
        var response = await CreateUserRoleClient().GetAsync("api/scheduler-dashboard/health", CancellationToken);
        response.EnsureSuccessStatusCode();
        var health = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken);

        var row = health.GetProperty(list).EnumerateArray()
            .FirstOrDefault(x => x.GetProperty("jobKey").GetString() == jobKey);

        row.ValueKind.Should().NotBe(JsonValueKind.Undefined, $"the seeded job belongs on {list}");
        return row;
    }

    private async Task<(string JobKey, long Id)> SeedJobAsync(Action<ScheduledJob> configure)
    {
        var unique = Guid.NewGuid().ToString("N")[..12];
        var job = new ScheduledJob
        {
            JobKey = $"tests.attention.{unique}",
            HandlerKey = PurgeExpiredRunLogsJobHandler.HandlerKey,
            OwnerModule = $"AttentionTest-{unique}",
            ScheduleType = JobScheduleType.Cron,
            Cron = "0 0 3 * * ?",
            Status = JobStatus.Active,
            NextRunAt = DateTime.UtcNow.AddHours(1)
        };
        configure(job);

        await using var db = CreateDbContext();
        db.Set<ScheduledJob>().Add(job);
        await db.SaveChangesAsync(CancellationToken);

        return (job.JobKey, job.Id);
    }

    private async Task<long> SeedRunAsync(long jobId, string jobKey, Action<ScheduledJobRun> configure)
    {
        var run = new ScheduledJobRun
        {
            ScheduledJobId = jobId,
            JobKeySnapshot = jobKey,
            HandlerKeySnapshot = PurgeExpiredRunLogsJobHandler.HandlerKey,
            StartedAt = DateTime.UtcNow,
            FinishedAt = DateTime.UtcNow,
            Outcome = RunOutcome.Succeeded,
            TriggerSource = TriggerSource.Scheduled,
            CorrelationId = Guid.NewGuid().ToString("N")
        };
        configure(run);

        await using var db = CreateDbContext();
        db.Set<ScheduledJobRun>().Add(run);
        await db.SaveChangesAsync(CancellationToken);

        return run.Id;
    }
}
