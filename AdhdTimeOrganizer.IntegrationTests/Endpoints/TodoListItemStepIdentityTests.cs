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
/// TEST-14 — the gap that made <c>CQ-19</c> invisible was that nothing did a PUT of an item and <b>then</b>
/// used one of its step ids: <c>AfterMapping</c> rebuilt <c>Steps</c> from fresh instances, <c>Id</c> defaults
/// to <c>Guid.NewGuid()</c> on a <c>ValueGeneratedNever()</c> column, and the steps are owned and serialised
/// <c>ToJson</c> into the item's row — so a re-minted id was identity loss with no row churn, no FK and no
/// error to notice it by.
/// <para>
/// <see cref="TodoListItemUpdateOwnedFieldsTests"/> already pins the reconcile itself (ids kept, steps added
/// and dropped, completion carried across two PUTs) and all of <c>CQ-16</c>. This file covers the two things
/// it does not: the end-to-end route round trip — <c>PATCH .../steps/{stepId}/toggle</c> with an id that came
/// back from a PUT has to <b>work</b>, not merely avoid a 404 — and the one path through <c>AfterMapping</c>
/// that was always correct, a request that omits <c>Steps</c> entirely.
/// </para>
/// </summary>
[Collection("Postgres")]
public class TodoListItemStepIdentityTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    /// <summary>
    /// <c>(UserId, Priority)</c> is uniquely indexed and the whole Postgres collection shares one database and
    /// one test user, so each seeded rank has to be distinct. Band 340–369 is unclaimed by any other file.
    /// </summary>
    private static int _nextPriorityRank = 340;

    private long _activityId;
    private long _priorityId;

    [Fact(DisplayName = "CQ-19: a step id captured before a PUT still toggles afterwards -- 204, and the step actually ticks")]
    public async Task ToggleStep_AfterPutOfParentItem_TicksTheStep()
    {
        var stepId = Guid.NewGuid();
        long itemId;
        await using (var db = CreateDbContext())
            itemId = await SeedItemAsync(db, "step-identity-toggle", [new TodoListStep { Id = stepId, Name = "Only step", Order = 0 }]);

        var client = CreateClient();
        var put = await client.PutAsJsonAsync($"api/todo-list-item/{itemId}", Body(
            note: "an edit that has nothing to do with the checklist",
            steps: [new { id = stepId, name = "Only step", order = 0 }]), JsonOpts, CancellationToken);
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var toggle = await client.PatchAsync($"api/todo-list-item/{itemId}/steps/{stepId}/toggle", null, CancellationToken);

        toggle.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "this is the id the client is holding after saving the item -- a fresh Guid per PUT 404s a step the user can see on screen");

        var steps = await ReadStepsAsync(itemId);
        steps.Single(s => s.Id == stepId).IsDone.Should().BeTrue("the toggle has to reach the step, not just fail to 404");
    }

    /// <summary>
    /// The same carry-over as <c>UpdateItem_Twice_KeepsStepCompletion</c>, but with the tick arriving through
    /// the toggle route rather than the seed, and with a second step alongside it — so the item is genuinely
    /// half-finished and the second PUT is echoing exactly what the client read back from the first.
    /// </summary>
    [Fact(DisplayName = "CQ-19: a step ticked through the toggle route survives two consecutive PUTs of its parent")]
    public async Task ToggleThenPutTwice_KeepsTheTickAndTheIds()
    {
        var tickedId = Guid.NewGuid();
        var openId = Guid.NewGuid();
        long itemId;
        await using (var db = CreateDbContext())
            itemId = await SeedItemAsync(db, "step-identity-carry", [
                new TodoListStep { Id = tickedId, Name = "Ticked", Order = 0 },
                new TodoListStep { Id = openId, Name = "Open", Order = 1 }
            ]);

        var client = CreateClient();
        var toggle = await client.PatchAsync($"api/todo-list-item/{itemId}/steps/{tickedId}/toggle", null, CancellationToken);
        toggle.StatusCode.Should().Be(HttpStatusCode.NoContent);

        object[] echoed =
        [
            new { id = tickedId, name = "Ticked", order = 0 },
            new { id = openId, name = "Open", order = 1 }
        ];

        for (var i = 1; i <= 2; i++)
        {
            var put = await client.PutAsJsonAsync($"api/todo-list-item/{itemId}",
                Body(note: $"edit {i}", steps: echoed), JsonOpts, CancellationToken);
            put.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var steps = await ReadStepsAsync(itemId);
        steps.Select(s => s.Id).Should().BeEquivalentTo([tickedId, openId],
            "asserted as a set -- ordering is Order's job, not the collection's");
        steps.Single(s => s.Id == tickedId).IsDone.Should().BeTrue(
            "the carry-over reads the request's ids against the stored ones, so it only works for as long as those ids stay put");
        steps.Single(s => s.Id == openId).IsDone.Should().BeFalse();
    }

    [Fact(DisplayName = "A PUT that omits Steps leaves the collection untouched -- same ids, same IsDone, same count")]
    public async Task Put_WithoutSteps_LeavesTheChecklistAlone()
    {
        var tickedId = Guid.NewGuid();
        var openId = Guid.NewGuid();
        long itemId;
        await using (var db = CreateDbContext())
            itemId = await SeedItemAsync(db, "step-identity-omitted", [
                new TodoListStep { Id = tickedId, Name = "Ticked", Order = 0, IsDone = true },
                new TodoListStep { Id = openId, Name = "Open", Order = 1 }
            ]);

        // No isDone flip either: SetDone is a snap, so a completion change would rewrite every step's IsDone
        // and this would be pinning that instead of the null-Steps path.
        var put = await CreateClient().PutAsJsonAsync($"api/todo-list-item/{itemId}",
            Body(note: "just fixing a typo"), JsonOpts, CancellationToken);
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var steps = await ReadStepsAsync(itemId);
        steps.Should().HaveCount(2);
        steps.Select(s => s.Id).Should().BeEquivalentTo([tickedId, openId],
            "AfterMapping skips the reconcile when the request carries no Steps -- this is the one path through it that was always correct");
        steps.Single(s => s.Id == tickedId).IsDone.Should().BeTrue();
        steps.Single(s => s.Id == openId).IsDone.Should().BeFalse();
    }

    // ---- helpers -----------------------------------------------------------------------------------

    /// <summary>
    /// The PUT body. <c>activityId</c> and <c>taskPriorityId</c> are required on every request and say nothing
    /// about any of these cases; <c>steps</c> is omitted from the JSON entirely when null, which is the whole
    /// assertion in <see cref="Put_WithoutSteps_LeavesTheChecklistAlone"/>.
    /// </summary>
    private Dictionary<string, object?> Body(string? note = null, object[]? steps = null, bool isDone = false)
    {
        var body = new Dictionary<string, object?>
        {
            ["activityId"] = _activityId,
            ["taskPriorityId"] = _priorityId,
            ["isDone"] = isDone
        };

        if (note is not null) body["note"] = note;
        if (steps is not null) body["steps"] = steps;

        return body;
    }

    private async Task<long> SeedItemAsync(DbContext db, string label, List<TodoListStep> steps)
    {
        _activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"{label}-activity", ct: CancellationToken);
        _priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(
            db, Interlocked.Increment(ref _nextPriorityRank), text: $"{label}-priority", ct: CancellationToken);
        var listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, name: $"{label}-list", ct: CancellationToken);
        return await TodoListTestSeedHelper.SeedTodoListItemAsync(
            db, _activityId, _priorityId, listId, steps: steps, ct: CancellationToken);
    }

    private async Task<List<TodoListStep>> ReadStepsAsync(long itemId)
    {
        await using var db = CreateDbContext();
        var item = await db.Set<TodoListItem>().AsNoTracking().Include(i => i.Steps)
            .SingleAsync(i => i.Id == itemId, CancellationToken);
        return item.Steps.OrderBy(s => s.Order).ToList();
    }
}
