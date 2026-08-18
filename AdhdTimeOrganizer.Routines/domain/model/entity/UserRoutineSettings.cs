using AdhdTimeOrganizer.Core.domain.model.entity.user;

namespace AdhdTimeOrganizer.Routines.domain.model.entity.todoList;

/// <summary>
/// One row per user, holding the routine-domain preferences that are facts about the person rather than about
/// a browser — so they follow the user to a second device instead of living in that device's local storage.
/// </summary>
public class UserRoutineSettings : BaseEntityWithUser
{
    /// <summary>
    /// The week-start date the user last dismissed the weekly routine review ("fresh start") card for, or
    /// <c>null</c> if they never have.
    /// <para>
    /// Stored, never interpreted: whether this date *is* the week the user is currently looking at depends on
    /// their <c>FirstDayOfWeek</c> and on the time zone their device is in right now, and only the client
    /// knows both. The server's whole job here is to make the answer the same on every device — so nothing
    /// server-side compares this against "now", and no job reads it.
    /// </para>
    /// <para>
    /// This is a date because a dismissal is currently *for a week*. If the product rule ever becomes "don't
    /// nag me until the routines actually change", a date is the wrong value and this becomes a stamp of what
    /// was reviewed — the client cannot make that switch on its own, so it is a contract change, not a
    /// client-side reinterpretation of this field.
    /// </para>
    /// </summary>
    public DateOnly? RoutineReviewDismissedForWeekStart { get; set; }
}
