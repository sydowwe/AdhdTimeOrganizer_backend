using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-3 / Section F — <c>TaskImportance(UserId, Importance)</c> is unique (immediate, not deferred:
/// <c>TaskImportanceConfiguration</c> declares a plain <c>HasIndex(...).IsUnique()</c>, and Postgres checks a
/// non-deferred unique index per statement). Reordering is documented as "a swap, not an insert" — but
/// <b>there is no dedicated reorder/swap endpoint anywhere in the solution</b> (checked: only
/// Create/Update/Delete/GetAll/GetById/Grid/GetSelectOptions exist under
/// <c>application/endpoint/activityPlanning/taskImportance/</c>, and nothing host-side either). The only way a
/// client can reorder two rows is two independent <c>PUT</c> calls to <c>UpdateTaskImportanceEndpoint</c>, each
/// its own transaction — so a "swap" that sets one row directly to the other's current rank collides with the
/// live unique index before the second call ever runs.
/// <para>
/// These tests pin what actually happens: the naive one-statement approach fails cleanly (409, via
/// <c>DbUtils.HandleException</c> mapping <c>DbUniqueViolationError</c> → 409) rather than corrupting data or
/// 500ing, and the only way to actually reorder over this API is a three-step dance through a temporary
/// out-of-range value.
/// </para>
/// </summary>
[Collection("Postgres")]
public class TaskImportanceReorderTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact(DisplayName = "Naively PUTting one row onto another's live rank 409s, not 500s, and touches nothing")]
    public async Task NaiveReorder_DirectlyOntoLiveRank_Returns409AndLeavesBothRowsUnchanged()
    {
        long lowId, highId;
        await using (var db = CreateDbContext())
        {
            lowId = await PlanningTestSeedHelper.SeedTaskImportanceAsync(db, 1, text: "Low");
            highId = await PlanningTestSeedHelper.SeedTaskImportanceAsync(db, 2, text: "High");
        }

        var response = await CreateClient().PutAsJsonAsync($"api/task-importance/{lowId}", new
        {
            Text = "Low",
            Color = "#abcdef",
            Importance = 2 // collides with `highId`'s live rank
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "the unique index is checked immediately, not deferred, so this fails clean rather than corrupting or 500ing");

        await using var verifyDb = CreateDbContext();
        var low = await verifyDb.Set<TaskImportance>().SingleAsync(t => t.Id == lowId, CancellationToken);
        var high = await verifyDb.Set<TaskImportance>().SingleAsync(t => t.Id == highId, CancellationToken);
        low.Importance.Should().Be(1, "the failed request must not have partially applied");
        high.Importance.Should().Be(2);
    }

    [Fact(DisplayName = "Swapping two adjacent ranks over this API requires a temporary out-of-range value")]
    public async Task ReorderViaTemporaryValue_SwapsCleanly()
    {
        long lowId, highId;
        await using (var seedDb = CreateDbContext())
        {
            lowId = await PlanningTestSeedHelper.SeedTaskImportanceAsync(seedDb, 1, text: "Low");
            highId = await PlanningTestSeedHelper.SeedTaskImportanceAsync(seedDb, 2, text: "High");
        }

        var client = CreateClient();

        // Step 1: park `lowId` on a rank nothing else holds.
        (await client.PutAsJsonAsync($"api/task-importance/{lowId}", new { Text = "Low", Color = "#abcdef", Importance = -1 }, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 2: `highId` can now take the vacated rank 1.
        (await client.PutAsJsonAsync($"api/task-importance/{highId}", new { Text = "High", Color = "#abcdef", Importance = 1 }, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 3: `lowId` moves off the temporary value onto the now-vacated rank 2.
        (await client.PutAsJsonAsync($"api/task-importance/{lowId}", new { Text = "Low", Color = "#abcdef", Importance = 2 }, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<TaskImportance>().SingleAsync(t => t.Id == lowId, CancellationToken)).Importance.Should().Be(2);
        (await verifyDb.Set<TaskImportance>().SingleAsync(t => t.Id == highId, CancellationToken)).Importance.Should().Be(1);
    }

    [Fact(DisplayName = "The 666 Optional sentinel survives neighbouring reorders untouched")]
    public async Task OptionalSentinel_SurvivesNeighbouringReorders()
    {
        long optionalId, aId, bId;
        await using (var seedDb = CreateDbContext())
        {
            optionalId = await PlanningTestSeedHelper.SeedTaskImportanceAsync(seedDb, TaskImportance.OptionalMarkerValue, text: "Optional");
            aId = await PlanningTestSeedHelper.SeedTaskImportanceAsync(seedDb, 1, text: "A");
            bId = await PlanningTestSeedHelper.SeedTaskImportanceAsync(seedDb, 2, text: "B");
        }

        var client = CreateClient();
        (await client.PutAsJsonAsync($"api/task-importance/{aId}", new { Text = "A", Color = "#abcdef", Importance = -1 }, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PutAsJsonAsync($"api/task-importance/{bId}", new { Text = "B", Color = "#abcdef", Importance = 1 }, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PutAsJsonAsync($"api/task-importance/{aId}", new { Text = "A", Color = "#abcdef", Importance = 2 }, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = CreateDbContext();
        var optional = await db.Set<TaskImportance>().SingleAsync(t => t.Id == optionalId, CancellationToken);
        optional.Importance.Should().Be(TaskImportance.OptionalMarkerValue);
        optional.IsOptionalMarker().Should().BeTrue();
    }
}

file static class TaskImportanceAssertionExtensions
{
    // BasePlannerTask.IsOptional reads Importance?.Importance == OptionalMarkerValue off a *task's* linked
    // importance; TaskImportance itself has no such property, so this mirrors the sentinel check directly.
    public static bool IsOptionalMarker(this TaskImportance importance) => importance.Importance == TaskImportance.OptionalMarkerValue;
}
