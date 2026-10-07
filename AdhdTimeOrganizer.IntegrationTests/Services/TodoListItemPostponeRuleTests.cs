using AdhdTimeOrganizer.TodoLists.application.service;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Services;

/// <summary>
/// FO3 phase 2's to-do rule without a database: which due-date moves are postponements. Both write paths (the PUT
/// and the renegotiation endpoint) are pinned over HTTP in <c>Endpoints.RescheduleOverdueTodoListItemTests</c>.
/// </summary>
public class TodoListItemPostponeRuleTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    private static TodoListItem Item() => new() { TaskPriorityId = 1 };

    [Fact(DisplayName = "FO3: pushing an overdue due date later is a postponement, and fixes the first due date")]
    public void OverduePushedLater_Counts()
    {
        var item = Item();

        TodoListItemPostponeRule.Apply(item, false, Today.AddDays(-2), Today, Today).Should().BeTrue();

        item.PostponeCount.Should().Be(1);
        item.FirstDueDate.Should().Be(Today.AddDays(-2));
    }

    [Fact(DisplayName = "FO3: moving a deadline that has not arrived, or one due today, is planning")]
    public void NotYetOverdue_DoesNotCount()
    {
        var item = Item();

        TodoListItemPostponeRule.Apply(item, false, Today, Today.AddDays(3), Today).Should().BeFalse("due today is not overdue yet");
        TodoListItemPostponeRule.Apply(item, false, Today.AddDays(2), Today.AddDays(9), Today).Should().BeFalse();

        item.PostponeCount.Should().Be(0);
        item.FirstDueDate.Should().BeNull();
    }

    [Fact(DisplayName = "FO3: setting, clearing or pulling a due date earlier never counts")]
    public void SetClearOrEarlier_DoesNotCount()
    {
        var item = Item();

        TodoListItemPostponeRule.Apply(item, false, null, Today, Today).Should().BeFalse("setting a date is planning");
        TodoListItemPostponeRule.Apply(item, false, Today.AddDays(-2), null, Today).Should().BeFalse("clearing it is letting go");
        TodoListItemPostponeRule.Apply(item, false, Today.AddDays(-2), Today.AddDays(-3), Today).Should().BeFalse();

        item.PostponeCount.Should().Be(0);
    }

    [Fact(DisplayName = "FO3: a done item is not postponed")]
    public void DoneItem_DoesNotCount() =>
        TodoListItemPostponeRule.Apply(Item(), true, Today.AddDays(-2), Today, Today).Should().BeFalse();

    [Fact(DisplayName = "FO3: once an item has slipped, pushing it again counts even before the new date arrives")]
    public void AfterTheFirstSlip_EveryPushCounts()
    {
        var item = Item();
        TodoListItemPostponeRule.Apply(item, false, Today.AddDays(-2), Today.AddDays(1), Today).Should().BeTrue();

        TodoListItemPostponeRule.Apply(item, false, Today.AddDays(1), Today.AddDays(7), Today).Should().BeTrue();

        item.PostponeCount.Should().Be(2);
        item.FirstDueDate.Should().Be(Today.AddDays(-2), "set once");
    }
}
