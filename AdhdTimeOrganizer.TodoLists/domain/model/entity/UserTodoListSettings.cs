using AdhdTimeOrganizer.Core.domain.model.entity.user;

namespace AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;

/// <summary>
/// One row per user, holding the to-do preferences that are facts about the person rather than about a browser.
/// A user who never touched them has no row and gets <see cref="DefaultDueReminderMinutesBefore"/> — the row is
/// written by the settings PUT, never by a read.
/// </summary>
public class UserTodoListSettings : BaseEntityWithUser
{
    /// <summary>What a user with no row gets, and what a new row starts at.</summary>
    public const int DefaultDueReminderMinutesBefore = 30;

    /// <summary>The longest lead the validator accepts: a day before is the furthest a "due at 14:00" nudge is still about the item.</summary>
    public const int MaxDueReminderMinutesBefore = 24 * 60;

    /// <summary>
    /// How long before a to-do's due instant (its <c>DueDate</c> + <c>DueTime</c>, in the user's zone) the
    /// <c>TodoItemDue</c> reminder fires. 0 fires at the instant itself. A date-only item never has one.
    /// <para>
    /// Muting to-do reminders altogether is not a value here: it is the <c>("TodoLists", "TodoItemDue")</c> switch
    /// on <c>/reminder-preference</c>, the same place every other reminder kind is muted.
    /// </para>
    /// <para>
    /// Changing it re-registers every open timed item the user has — a registered reminder carries the lead it
    /// was built with, and only a re-registration moves it.
    /// </para>
    /// </summary>
    public int DueReminderMinutesBefore { get; set; } = DefaultDueReminderMinutesBefore;
}
