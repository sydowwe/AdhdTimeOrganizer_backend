using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.Testing;
using Sydowwe.Scheduler.application.job;
using Sydowwe.Scheduler.domain.entity;
using Sydowwe.Scheduler.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Infrastructure;

/// <summary>
/// Pins the two process-level signals the dashboard renders — <c>isOrphaned</c> and <c>isOverdue</c> — on the
/// rows themselves, not on the routes.
/// <para>
/// Both fail silently by construction. Neither is reachable from the entity, so both are overlaid <i>after</i>
/// the projection, and both are non-nullable: a surface that forgets the overlay does not omit the field, it
/// answers <c>false</c>. That is not a visibly broken response — it is a confident wrong one, and it is exactly
/// what shipped before: the job-detail page told operators "On schedule" about a job five hours late, while the
/// <c>onlyOverdue</c> filter one request earlier had picked that same job out as the only late one in the
/// registry. A route test passes either way; only asserting on the returned row catches it.
/// </para>
/// <para>
/// The orphan half is worse than cosmetic: <c>JobActionButtons</c> gates Trigger-now on it, so a false there
/// offers an action whose 409 the server can already predict. Hence the by-id surface is pinned too — it is the
/// one the detail page reads, and it is the one base that has no projection hook, so its overlay lives in a
/// different place from the grids' and can drift independently.
/// </para>
/// </summary>
[Collection("Postgres")]
public class SchedulerJobSignalsTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    // A key no module claims. Orphanhood is decided against the DI handler set, so an unregisterable key is the
    // only way to seed the condition without tearing a real handler out of the container.
    private const string MissingHandlerKey = "Tests.HandlerThatWasRemoved";

    [Fact(DisplayName = "A job past its next run reports isOverdue on the grid — the same rows onlyOverdue selects")]
    public async Task OverdueJobReportsTheFlagTheFilterAgreesWith()
    {
        var owner = await SeedAsync(job =>
        {
            job.Status = JobStatus.Active;
            job.NextRunAt = DateTime.UtcNow.AddHours(-5);
        });

        var unfiltered = await GridAsync(owner);
        Flag(unfiltered.Single(), "isOverdue").Should().BeTrue(
            "the row the filter calls overdue must say so, or filtering and reading the chips contradict each other");

        var filtered = await GridAsync(owner, onlyOverdue: true);
        filtered.Should().HaveCount(1, "the seeded job is the only overdue one in its owner module");
        Flag(filtered.Single(), "isOverdue").Should().BeTrue();
    }

    [Fact(DisplayName = "A job inside the grace margin is not overdue — the margin is the display one, 60s")]
    public async Task AJobJustPastItsNextRunIsNotYetOverdue()
    {
        var owner = await SeedAsync(job =>
        {
            job.Status = JobStatus.Active;
            // Inside the 60s margin that exists to absorb the gap between a fire and the dispatcher writing
            // the new NextRunAt. Flagging this row would make every healthy job blink overdue on each fire.
            job.NextRunAt = DateTime.UtcNow.AddSeconds(-5);
        });

        Flag((await GridAsync(owner)).Single(), "isOverdue").Should().BeFalse();
    }

    [Fact(DisplayName = "A paused job is never overdue, however far past its last known next run")]
    public async Task APausedJobIsNeverOverdue()
    {
        var owner = await SeedAsync(job =>
        {
            job.Status = JobStatus.Paused;
            job.NextRunAt = DateTime.UtcNow.AddDays(-3);
        });

        Flag((await GridAsync(owner)).Single(), "isOverdue").Should().BeFalse(
            "a paused job has no fire expectation, so it cannot be late for one");
    }

    [Fact(DisplayName = "A job whose handler is gone reports isOrphaned on the grid and on the by-id read")]
    public async Task OrphanedJobReportsTheFlagOnEverySurfaceTheDetailPageUses()
    {
        var (owner, id) = await SeedCoreAsync(job => job.HandlerKey = MissingHandlerKey);

        Flag((await GridAsync(owner)).Single(), "isOrphaned").Should().BeTrue();

        var byId = await ReadJobAsync(id);
        Flag(byId, "isOrphaned").Should().BeTrue(
            "the detail page gates Trigger-now on this — a false here offers an action the server already knows 409s");
    }

    [Fact(DisplayName = "A job whose handler is registered is not orphaned")]
    public async Task ARegisteredHandlerIsNotOrphaned()
    {
        RegisteredHandlerKeys().Should().Contain(PurgeExpiredRunLogsJobHandler.HandlerKey,
            "the negative case is only meaningful if this key really is in the container");

        var owner = await SeedAsync(job => job.HandlerKey = PurgeExpiredRunLogsJobHandler.HandlerKey);

        Flag((await GridAsync(owner)).Single(), "isOrphaned").Should().BeFalse();
    }

    [Fact(DisplayName = "A Removed job is not reported as orphaned — that is its intended end state")]
    public async Task ARemovedJobIsNotOrphaned()
    {
        var owner = await SeedAsync(job =>
        {
            job.HandlerKey = MissingHandlerKey;
            job.Status = JobStatus.Removed;
            job.NextRunAt = null;
        });

        Flag((await GridAsync(owner)).Single(), "isOrphaned").Should().BeFalse(
            "listing every retired job as a problem buries the rows that actually need acting on");
    }

    [Fact(DisplayName = "A job with a run in flight reports runningSince on the grid and on the by-id read")]
    public async Task InFlightJobReportsRunningSinceOnBothSurfaces()
    {
        var startedAt = DateTime.UtcNow.AddMinutes(-7);
        var (owner, id) = await SeedCoreAsync(job =>
        {
            job.DisallowConcurrent = true;
            job.RunningSince = startedAt;
        });

        // The trigger-now confirm warns of a likely veto off this. Dropped from a projection it serializes null,
        // which reads as "nothing running" — the same confident-wrong shape as the two flags above.
        RunningSince((await GridAsync(owner)).Single()).Should().BeCloseTo(startedAt, TimeSpan.FromSeconds(1));
        RunningSince(await ReadJobAsync(id)).Should().BeCloseTo(startedAt, TimeSpan.FromSeconds(1));
    }

    [Fact(DisplayName = "A job with nothing in flight reports runningSince null")]
    public async Task IdleJobReportsNoRunningSince()
    {
        var owner = await SeedAsync(job => job.RunningSince = null);

        (await GridAsync(owner)).Single().GetProperty("runningSince").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ---- helpers -----------------------------------------------------------------------------------

    private static DateTime RunningSince(JsonElement row) => row.GetProperty("runningSince").GetDateTime().ToUniversalTime();

    // Handlers are scoped (IScopedService), so they only resolve inside a scope — the same way an endpoint sees
    // them. Resolving off the root provider throws.
    private IReadOnlyList<string> RegisteredHandlerKeys()
    {
        using var scope = Fixture.AdminAndUserFactory.Services.CreateScope();
        return [.. scope.ServiceProvider.GetRequiredService<IEnumerable<IScheduledJobHandler>>().Select(h => h.Key)];
    }

    private static bool Flag(JsonElement row, string name) => row.GetProperty(name).GetBoolean();

    private async Task<List<JsonElement>> GridAsync(string ownerModule, bool? onlyOverdue = null)
    {
        var request = new
        {
            page = 1,
            itemsPerPage = 25,
            sortBy = Array.Empty<object>(),
            useFilter = true,
            filter = new { ownerModule, onlyOverdue }
        };

        var response = await CreateUserRoleClient().PostAsJsonAsync("api/scheduler-dashboard/jobs-overview", request, JsonOpts, CancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken);
        return [.. body.GetProperty("items").EnumerateArray()];
    }

    private async Task<JsonElement> ReadJobAsync(long id)
    {
        var response = await CreateUserRoleClient().GetAsync($"api/scheduled-job/{id}", CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken);
    }

    /// <summary>Seeds one registry row under its own owner module, which is how each test isolates its rows.</summary>
    private async Task<string> SeedAsync(Action<ScheduledJob> configure) => (await SeedCoreAsync(configure)).Owner;

    private async Task<(string Owner, long Id)> SeedCoreAsync(Action<ScheduledJob> configure)
    {
        var unique = Guid.NewGuid().ToString("N")[..12];
        var job = new ScheduledJob
        {
            JobKey = $"tests.signals.{unique}",
            HandlerKey = PurgeExpiredRunLogsJobHandler.HandlerKey,
            OwnerModule = $"SignalsTest-{unique}",
            ScheduleType = JobScheduleType.Cron,
            Cron = "0 0 3 * * ?",
            Status = JobStatus.Active,
            NextRunAt = DateTime.UtcNow.AddHours(1)
        };
        configure(job);

        await using var db = CreateDbContext();
        db.Set<ScheduledJob>().Add(job);
        await db.SaveChangesAsync(CancellationToken);

        return (job.OwnerModule, job.Id);
    }
}
