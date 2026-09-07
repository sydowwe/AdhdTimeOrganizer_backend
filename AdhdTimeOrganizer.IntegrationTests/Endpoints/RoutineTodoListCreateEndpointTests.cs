using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// `POST /api/routine-todo-list` — the part of the create path that is routine-specific rather than
/// inherited from `BaseCreateEndpoint`.
/// </summary>
/// <remarks>
/// `AfterMapping` derives `DisplayOrder` through the routine-shaped `GetNextDisplayOrder`
/// (`RoutineTodoListExtensions`), which groups on `TimePeriodId` — a *different* overload from the generic
/// one every other to-do item uses, and the reason that overload had to move out of the TodoLists slice.
/// Dropping the grouping compiles, keeps returning descending numbers and looks entirely correct in a
/// single-period fixture; it only shows up as two periods whose orders interleave, which is why the
/// fixture below needs two.
/// <para>
/// Ownership of the supplied `TimePeriodId` is <see cref="RoutineTodoListPeriodRulesTests"/>; the generic
/// create cases (role gate, happy path) are `TodoListCrudAuthMatrixTests`.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class RoutineTodoListCreateEndpointTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact(DisplayName = "Create derives DisplayOrder within the item's own period, not across all of them")]
    public async Task CreateRoutineTodoList_SetsDisplayOrderWithinItsPeriod()
    {
        long firstInA, secondInA, firstInB, periodA, periodB;
        await using (var db = CreateDbContext())
        {
            // Distinct lengths: (UserId, LengthInDays) is unique on a period.
            periodA = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, lengthInDays: 7, ct: CancellationToken);
            periodB = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, lengthInDays: 14, ct: CancellationToken);

            // One activity per item: an activity can sit in a routine period only once, and it also gives each
            // created row a stable handle to read back without going through the response DTO (CQ-5).
            firstInA = await PlanningTestSeedHelper.SeedActivityAsync(db, "First in A", ct: CancellationToken);
            secondInA = await PlanningTestSeedHelper.SeedActivityAsync(db, "Second in A", ct: CancellationToken);
            firstInB = await PlanningTestSeedHelper.SeedActivityAsync(db, "First in B", ct: CancellationToken);
        }

        var client = CreateUserRoleClient();
        await PostAsync(client, firstInA, periodA);
        await PostAsync(client, secondInA, periodA);
        await PostAsync(client, firstInB, periodB);

        await using var verify = CreateDbContext();
        var items = await verify.Set<RoutineTodoList>().AsNoTracking()
            .Where(r => r.ActivityId == firstInA || r.ActivityId == secondInA || r.ActivityId == firstInB)
            .ToDictionaryAsync(r => r.ActivityId, CancellationToken);

        items.Should().HaveCount(3);

        // Orders descend within a period: the second item in A has to sit below the first.
        items[secondInA].DisplayOrder.Should().BeLessThan(items[firstInA].DisplayOrder,
            "a second item in the same period is ordered beneath the one already there");

        // The load-bearing assertion. B is empty when its first item is created, so that item must get the
        // same opening order A's first item got. Compared to each other rather than to a literal, because
        // DisplayOrderStart is MaxItems * DisplayOrderGap and both come from configuration.
        items[firstInB].DisplayOrder.Should().Be(items[firstInA].DisplayOrder,
            "each period opens its own ordering — an item in B must not be pushed down by A's items");
    }

    private async Task PostAsync(HttpClient client, long activityId, long timePeriodId)
    {
        var response = await client.PostAsJsonAsync(
            "/api/routine-todo-list",
            new { activityId, timePeriodId },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
