using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-4 / Section F — <c>TaskPriority(UserId, Priority)</c> is unique
/// (<c>TaskPriorityConfiguration.cs:19</c>), so reordering two priorities is meant to be a swap, mirroring
/// <see cref="TaskImportanceReorderTests"/>'s "temporary out-of-range value" three-step dance (there is no
/// dedicated reorder endpoint for TaskPriority either — only PUT). The index is checked immediately, not
/// deferred, so a naive single PUT onto a live rank fails clean with 409 rather than corrupting data or
/// 500ing, and the only way to actually reorder over this API is the three-step dance through a temporary
/// out-of-range value.
/// <para>
/// Unlike <c>TaskImportance</c> (no floor), <c>TaskPriorityValidator</c> requires <c>Priority &gt;= 1</c>,
/// so the "temporary value" here has to be a large unused rank rather than a negative one.
/// </para>
/// </summary>
[Collection("Postgres")]
public class TaskPriorityReorderTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact(DisplayName = "Naively PUTting one row onto another's live rank 409s, not 500s, and touches nothing")]
    public async Task NaiveReorder_DirectlyOntoLiveRank_Returns409AndLeavesBothRowsUnchanged()
    {
        long lowId, highId;
        await using (var db = CreateDbContext())
        {
            lowId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 1, text: "Low");
            highId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 2, text: "High");
        }

        var response = await CreateClient().PutAsJsonAsync($"api/task-priority/{lowId}", new
        {
            Text = "Low",
            Color = "#abcdef",
            Priority = (short)2 // collides with `highId`'s live rank
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "the unique index is checked immediately, not deferred, so this fails clean rather than corrupting or 500ing");

        await using var verifyDb = CreateDbContext();
        var low = await verifyDb.Set<TaskPriority>().SingleAsync(t => t.Id == lowId, CancellationToken);
        var high = await verifyDb.Set<TaskPriority>().SingleAsync(t => t.Id == highId, CancellationToken);
        low.Priority.Should().Be(1, "the failed request must not have partially applied");
        high.Priority.Should().Be(2);
    }

    [Fact(DisplayName = "Swapping two adjacent ranks over this API requires a temporary out-of-range value")]
    public async Task ReorderViaTemporaryValue_SwapsCleanly()
    {
        long lowId, highId;
        await using (var seedDb = CreateDbContext())
        {
            lowId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(seedDb, 1, text: "Low");
            highId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(seedDb, 2, text: "High");
        }

        var client = CreateClient();

        // Step 1: park `lowId` on a rank nothing else holds. TaskPriorityValidator requires Priority >= 1
        // (unlike TaskImportance), so the temporary value has to be a large unused rank, not a negative one.
        (await client.PutAsJsonAsync($"api/task-priority/{lowId}", new { Text = "Low", Color = "#abcdef", Priority = (short)9999 }, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 2: `highId` can now take the vacated rank 1.
        (await client.PutAsJsonAsync($"api/task-priority/{highId}", new { Text = "High", Color = "#abcdef", Priority = (short)1 }, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 3: `lowId` moves off the temporary value onto the now-vacated rank 2.
        (await client.PutAsJsonAsync($"api/task-priority/{lowId}", new { Text = "Low", Color = "#abcdef", Priority = (short)2 }, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<TaskPriority>().SingleAsync(t => t.Id == lowId, CancellationToken)).Priority.Should().Be(2);
        (await verifyDb.Set<TaskPriority>().SingleAsync(t => t.Id == highId, CancellationToken)).Priority.Should().Be(1);
    }
}
