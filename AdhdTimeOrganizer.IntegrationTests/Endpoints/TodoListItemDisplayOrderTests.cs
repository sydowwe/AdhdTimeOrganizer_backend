using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.infrastructure.settings;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// <c>TEST-10</c> — the arithmetic <c>TodoListExtensions.GetNextDisplayOrder</c> mints a new item's
/// <c>DisplayOrder</c> with, exercised through <c>POST /api/todo-list-item</c> because that is the only
/// production caller of the <c>taskPriorityId</c> overload.
/// </summary>
/// <remarks>
/// <para>
/// The silent failure this pins is that <b>nothing downstream notices a wrong <c>DisplayOrder</c></b>.
/// The create still answers 201, the row still exists, every foreign key still resolves; the only symptom
/// is that the user's list comes back in an order they did not choose, one release later. So every
/// assertion here is on the minted column value, never on the status code.
/// </para>
/// <para>
/// The other thing this file is the only proof of: <b><c>TodoListSettings</c> is actually bound</b>.
/// <c>Program.cs</c> does <c>services.Configure&lt;TodoListSettings&gt;(configuration.GetSection(...))</c>,
/// and a mistyped section name is not an error — it yields <c>MaxItems = 0</c> and
/// <c>DisplayOrderGap = 0</c>, i.e. every item in the database minted at order <c>0</c> and the whole
/// ordering feature quietly inert. <see cref="FirstItemInAnEmptyGroup_GetsDisplayOrderStart"/> asserts the
/// non-zero-ness explicitly for that reason.
/// </para>
/// <para>
/// Values come from <c>IOptions&lt;TodoListSettings&gt;</c> off the running host rather than from literals:
/// <c>DisplayOrderGap</c> and the derived <c>DisplayOrderStart</c> are configuration, so a hard-coded
/// <c>100_000_000</c> would fail on an <c>appsettings.json</c> edit instead of on a regression.
/// </para>
/// <para>
/// <b>Deliberately not duplicated here:</b> the two boundary defects in the same two lines —
/// <c>CQ-2</c> (a group whose lowest item sits at <c>0</c> must not be read as empty) and <c>CQ-3</c>
/// (<c>DisplayOrder</c> is a <c>bigint</c> and must not be cast to <c>int</c> in the <c>MIN</c>
/// projection) — are pinned by <see cref="TodoListDisplayOrderMintingTests"/>. This file covers the
/// ordinary ladder those two are the edges of.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class TodoListItemDisplayOrderTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private const string ItemRoute = "/api/todo-list-item";

    // task_priority is unique on (UserId, Priority) and the whole collection shares one database and one
    // test user, so each file claims a band (310-319, 320-329, 340-349, 380-389, ...). This file's is
    // 330-339.
    private const int PriorityRankA = 330;
    private const int PriorityRankB = 331;

    private long _listId;
    private long _otherListId;
    private long _priorityAId;
    private long _priorityBId;

    /// <summary>
    /// Trap 1 of the slice: <c>todo_list_item</c> is unique on <c>(UserId, ActivityId, TodoListId)</c>, so
    /// every item a test creates needs an activity of its own. Seeded up front in one batch rather than
    /// inside the loops, so a collision cannot be mistaken for an ordering failure.
    /// </summary>
    private readonly long[] _activityIds = new long[4];

    protected override async Task SeedAsync(DbContext db)
    {
        _listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "display-order-list", ct: CancellationToken);
        _otherListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "display-order-destination", ct: CancellationToken);

        _priorityAId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, PriorityRankA, UserId, "display-order-a", CancellationToken);
        _priorityBId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, PriorityRankB, UserId, "display-order-b", CancellationToken);

        for (var i = 0; i < _activityIds.Length; i++)
            _activityIds[i] = await PlanningTestSeedHelper.SeedActivityAsync(db, $"display-order-{i}", UserId, CancellationToken);
    }

    /// <summary>The host's own bound settings — see the remarks on why these are not literals.</summary>
    private TodoListSettings Settings =>
        Fixture.UserFactory.Services.GetRequiredService<IOptions<TodoListSettings>>().Value;

    // -------------------------------------------------------------------------------------------------
    // 1. The empty group, and the only proof that TodoListSettings is bound at all.
    // -------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "The first item in an empty priority group is minted at DisplayOrderStart — which is also the only assertion that TodoListSettings is bound")]
    public async Task FirstItemInAnEmptyGroup_GetsDisplayOrderStart()
    {
        var settings = Settings;

        // Asserted before the create, so a mis-bound section fails here with the reason rather than
        // downstream as "expected 100000000, found 0".
        settings.DisplayOrderGap.Should().BeGreaterThan(0,
            "a mistyped configuration section name binds silently and leaves DisplayOrderGap at 0, which mints every item at the same order");
        settings.MaxItems.Should().BeGreaterThan(0,
            "MaxItems is the other half of the derived DisplayOrderStart, and is 0 under the same mis-binding");

        var order = await CreateAndReadOrderAsync(_activityIds[0], _priorityAId);

        order.Should().Be(settings.DisplayOrderStart,
            "MIN over an empty group is null, which is the signal to start the ladder at the top rather than to subtract a gap");
    }

    // -------------------------------------------------------------------------------------------------
    // 2. The ladder itself.
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Three creates rather than two: two would pass against an implementation that subtracts the gap from
    /// <c>DisplayOrderStart</c> every time instead of from the group's current minimum.
    /// </summary>
    [Fact(DisplayName = "Successive items in one priority group descend by exactly one DisplayOrderGap")]
    public async Task SuccessiveItems_DescendByOneGap()
    {
        var settings = Settings;

        var first = await CreateAndReadOrderAsync(_activityIds[0], _priorityAId);
        var second = await CreateAndReadOrderAsync(_activityIds[1], _priorityAId);
        var third = await CreateAndReadOrderAsync(_activityIds[2], _priorityAId);

        new[] { first, second, third }.Should().Equal(
            new[]
            {
                settings.DisplayOrderStart,
                settings.DisplayOrderStart - settings.DisplayOrderGap,
                settings.DisplayOrderStart - 2 * settings.DisplayOrderGap
            },
            "each new item is minted one gap below the group's current minimum, in creation order");
    }

    // -------------------------------------------------------------------------------------------------
    // 3. The group boundary.
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// The group is <c>TaskPriorityId</c>, not the list and not the user's items at large. Guards against
    /// the <c>taskPriorityId</c> overload being "simplified" away into the ungrouped one, which would make
    /// every priority share a single descending ladder — invisible until two groups interleave.
    /// </summary>
    [Fact(DisplayName = "Items in different priority groups do not share a ladder — each group starts at DisplayOrderStart")]
    public async Task ItemsInDifferentPriorityGroups_DoNotInteract()
    {
        var settings = Settings;

        var inA = await CreateAndReadOrderAsync(_activityIds[0], _priorityAId);
        var inB = await CreateAndReadOrderAsync(_activityIds[1], _priorityBId);

        inA.Should().Be(settings.DisplayOrderStart);
        inB.Should().Be(settings.DisplayOrderStart,
            "priority B's group is still empty — the MIN is filtered by TaskPriorityId, so priority A's item is not in it");
    }

    // -------------------------------------------------------------------------------------------------
    // 6. The other overload, reached only through the move endpoint.
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>MoveTodoListItemEndpoint</c> calls the <em>generic</em> <c>GetNextDisplayOrder</c> overload with
    /// <c>e =&gt; e.TodoListId == destinationListId</c> — same body, different group key, and a second place
    /// the settings have to be bound. One assertion is enough: the move endpoint's own behaviour is covered
    /// by <c>RoutineGroupedReadAndMoveItemTests</c>, and what is specific to the ordering is only that the
    /// item is re-minted for the destination rather than carrying the order it held where it came from.
    /// </summary>
    [Fact(DisplayName = "Moving an item to an empty list re-mints its DisplayOrder for the destination group, not the priority group")]
    public async Task MoveToEmptyList_MintsDisplayOrderStartForThatList()
    {
        var settings = Settings;

        // Two items in the source list so the moved one is NOT at DisplayOrderStart beforehand — otherwise
        // "was re-minted" and "kept its old order" are the same number.
        await CreateAndReadOrderAsync(_activityIds[0], _priorityAId);
        var movedId = await CreateAsync(_activityIds[1], _priorityAId);
        (await ReadOrderAsync(movedId)).Should().Be(settings.DisplayOrderStart - settings.DisplayOrderGap);

        var response = await CreateUserRoleClient().PatchAsJsonAsync(
            $"{ItemRoute}/{movedId}/move",
            new { destinationListId = _otherListId },
            JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ReadOrderAsync(movedId)).Should().Be(settings.DisplayOrderStart,
            "the destination list holds nothing, and DisplayOrder is only meaningful within the group it was minted for");
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private async Task<long> CreateAsync(long activityId, long taskPriorityId)
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(
            ItemRoute,
            new { activityId, taskPriorityId, todoListId = _listId },
            JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<long>(JsonOpts, CancellationToken);
    }

    private async Task<long> CreateAndReadOrderAsync(long activityId, long taskPriorityId) =>
        await ReadOrderAsync(await CreateAsync(activityId, taskPriorityId));

    /// <summary>Always a fresh context: the value under test is written by the endpoint, not by this test.</summary>
    private async Task<long> ReadOrderAsync(long itemId)
    {
        await using var db = CreateDbContext();
        return await db.Set<TodoListItem>().IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.Id == itemId)
            .Select(i => i.DisplayOrder)
            .SingleAsync(CancellationToken);
    }
}
