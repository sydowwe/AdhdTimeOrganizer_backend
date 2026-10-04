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
/// Pins <c>excludeRemoved</c> on the jobs-overview grid. The dashboard sends it by default, so a predicate that
/// quietly stopped applying would put every spent one-shot back on the first page, and one that ignored an
/// explicit <c>status</c> would answer the needs-attention "Removed" count card with an empty grid. Both are
/// silent, so both are asserted on the returned rows.
/// </summary>
[Collection("Postgres")]
public class SchedulerJobsOverviewExcludeRemovedTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact(DisplayName = "excludeRemoved leaves out Removed jobs and keeps Active and Paused ones")]
    public async Task ExcludeRemovedDropsOnlyRemovedRows()
    {
        var owner = await SeedAsync(JobStatus.Active, JobStatus.Paused, JobStatus.Removed);

        var rows = await GridAsync(new { ownerModule = owner, excludeRemoved = true });

        rows.Select(Status).Should().BeEquivalentTo(["Active", "Paused"]);
    }

    [Fact(DisplayName = "Without excludeRemoved every status is listed — a client that does not send it sees no change")]
    public async Task OmittingTheFlagKeepsEveryStatus()
    {
        var owner = await SeedAsync(JobStatus.Active, JobStatus.Paused, JobStatus.Removed);

        var rows = await GridAsync(new { ownerModule = owner });

        rows.Select(Status).Should().BeEquivalentTo(["Active", "Paused", "Removed"]);
    }

    [Fact(DisplayName = "An explicit status=Removed wins over excludeRemoved")]
    public async Task AnExplicitStatusWinsOverTheExclusion()
    {
        var owner = await SeedAsync(JobStatus.Active, JobStatus.Removed);

        var rows = await GridAsync(new { ownerModule = owner, status = "Removed", excludeRemoved = true });

        rows.Select(Status).Should().BeEquivalentTo(["Removed"],
            "the needs-attention Removed card links here with the default exclusion still on");
    }

    // ---- helpers -----------------------------------------------------------------------------------

    private static string Status(JsonElement row) => row.GetProperty("status").GetString()!;

    private async Task<List<JsonElement>> GridAsync(object filter)
    {
        var request = new
        {
            page = 1,
            itemsPerPage = 25,
            sortBy = Array.Empty<object>(),
            useFilter = true,
            filter
        };

        var response = await CreateUserRoleClient().PostAsJsonAsync("api/scheduler-dashboard/jobs-overview", request, JsonOpts, CancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken);
        return [.. body.GetProperty("items").EnumerateArray()];
    }

    /// <summary>Seeds one job per status under a fresh owner module, which is how each test isolates its rows.</summary>
    private async Task<string> SeedAsync(params JobStatus[] statuses)
    {
        var unique = Guid.NewGuid().ToString("N")[..12];
        var owner = $"ExcludeRemovedTest-{unique}";

        await using var db = CreateDbContext();
        foreach (var status in statuses)
        {
            db.Set<ScheduledJob>().Add(new ScheduledJob
            {
                JobKey = $"tests.exclude-removed.{unique}.{status}",
                HandlerKey = PurgeExpiredRunLogsJobHandler.HandlerKey,
                OwnerModule = owner,
                ScheduleType = JobScheduleType.Cron,
                Cron = "0 0 3 * * ?",
                Status = status,
                NextRunAt = status == JobStatus.Removed ? null : DateTime.UtcNow.AddHours(1)
            });
        }
        await db.SaveChangesAsync(CancellationToken);

        return owner;
    }
}
