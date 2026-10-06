using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.TodoLists.application.service;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Services;

/// <summary>
/// FO3's rule, without a database: when an item counts as stuck, and which help is offered first. The endpoint
/// side (that the seam really sorts blocks into these counts, and that the token reaches all three reads) is
/// <c>Endpoints.TodoListItemPlanHelpTests</c>.
/// </summary>
public class TodoListItemPlanHelpRuleTests
{
    private static PlanHistory History(
        int completed = 0, int carriedOver = 0, int missed = 0,
        int noTime = 0, int noEnergy = 0, int notRelevant = 0, int other = 0) =>
        new(completed, carriedOver, missed, noTime, noEnergy, notRelevant, other);

    [Fact(DisplayName = "FO3: an item with no planner history is not stuck")]
    public void NoHistory_IsNotStuck() =>
        TodoListItemPlanHelp.For(null).Should().BeNull();

    [Fact(DisplayName = "FO3: two failed attempts are not enough; the third makes it stuck")]
    public void Threshold_IsThreeFailedAttempts()
    {
        TodoListItemPlanHelp.For(History(carriedOver: 1, missed: 1)).Should().BeNull();
        TodoListItemPlanHelp.For(History(carriedOver: 1, missed: 1, other: 1)).Should().Be(TodoListItemPlanHelp.SmallerStep);
    }

    [Fact(DisplayName = "FO3: one completed attempt clears it, however many failed")]
    public void AnyCompletion_ClearsIt() =>
        TodoListItemPlanHelp.For(History(completed: 1, carriedOver: 5, noTime: 3)).Should().BeNull();

    [Fact(DisplayName = "FO3: carry-overs, missed blocks, no-energy and free-text skips all point to a smaller step")]
    public void SlipsWithoutAReason_PointToASmallerStep()
    {
        TodoListItemPlanHelp.For(History(carriedOver: 3)).Should().Be(TodoListItemPlanHelp.SmallerStep);
        TodoListItemPlanHelp.For(History(missed: 3)).Should().Be(TodoListItemPlanHelp.SmallerStep);
        TodoListItemPlanHelp.For(History(noEnergy: 3)).Should().Be(TodoListItemPlanHelp.SmallerStep);
        TodoListItemPlanHelp.For(History(other: 3)).Should().Be(TodoListItemPlanHelp.SmallerStep);
    }

    [Fact(DisplayName = "FO3: mostly 'ran out of time' offers a shorter block")]
    public void MostlyNoTime_OffersAShorterBlock() =>
        TodoListItemPlanHelp.For(History(noTime: 2, missed: 1)).Should().Be(TodoListItemPlanHelp.ShorterBlock);

    [Fact(DisplayName = "FO3: mostly 'not relevant any more' offers to reconsider")]
    public void MostlyNotRelevant_OffersToReconsider() =>
        TodoListItemPlanHelp.For(History(notRelevant: 2, noTime: 1)).Should().Be(TodoListItemPlanHelp.Reconsider);

    /// <summary>
    /// Reconsidering suggests dropping the task, so it must clearly lead. A tie is the gentler offer.
    /// </summary>
    [Fact(DisplayName = "FO3: ties go to the smaller step, then the shorter block, never to reconsider")]
    public void Ties_FavourTheGentlerOffer()
    {
        TodoListItemPlanHelp.For(History(missed: 1, noTime: 1, notRelevant: 1)).Should().Be(TodoListItemPlanHelp.SmallerStep);
        TodoListItemPlanHelp.For(History(noTime: 2, notRelevant: 2)).Should().Be(TodoListItemPlanHelp.ShorterBlock);
    }
}
