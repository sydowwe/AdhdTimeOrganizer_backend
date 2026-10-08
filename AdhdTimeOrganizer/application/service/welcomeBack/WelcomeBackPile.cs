using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Notifications.domain.entity;

namespace AdhdTimeOrganizer.application.service.welcomeBack;

/// <summary>
/// FO5: what piled up while the user was away, defined once for the card (<c>GET /welcome-back</c>) and for the
/// fresh start that clears it (<c>POST /welcome-back/fresh-start</c>), so the two cannot disagree about which rows
/// the card was talking about.
/// <para>
/// <b>Host-side, reading four slices directly</b>, for the reason <c>GetAccountDeletionSummaryEndpoint</c> gives:
/// the answer spans TodoLists, Planning, Routines and Notifications, the host already references all of them and
/// owns <c>AppDbContext</c>, and a keyed seam per slice would be four string-keyed registrations that fail silently,
/// for a read nothing else performs. The rules the rows obey stay in their slices: the to-do postpone rule, the
/// carry-over record, the skip reasons and FO1's away rule are all called, not copied.
/// </para>
/// <para>
/// Every query scopes by <c>UserId</c> by hand as well as through the global filter, because
/// <see cref="Notification"/> is a module entity the filter does not cover.
/// </para>
/// </summary>
public static class WelcomeBackPile
{
    /// <summary>How many items each row of the card names before it says "and N more".</summary>
    public const int PreviewSize = 3;

    /// <summary>
    /// An open card: the absence and the user's today, all on their own clock.
    /// </summary>
    /// <param name="AwayFrom">The last activity before the absence (UTC).</param>
    /// <param name="ReturnedAt">The first activity after it (UTC).</param>
    /// <param name="AwayDate">The user-local date of <paramref name="AwayFrom"/>. Past tasks are counted from here.</param>
    /// <param name="ReturnDate">The user-local date of <paramref name="ReturnedAt"/>.</param>
    /// <param name="Today">The user's today, which may be after <paramref name="ReturnDate"/> if the card waited a day.</param>
    public sealed record Window(DateTime AwayFrom, DateTime ReturnedAt, DateOnly AwayDate, DateOnly ReturnDate, DateOnly Today);

    /// <summary>The user's open card, or null when there is none to show (see <see cref="UserPresence.CardIsOpen"/>).</summary>
    public static async Task<Window?> OpenWindowAsync(AppDbContext db, long userId, DateTime utcNow, CancellationToken ct)
    {
        var user = await db.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => new { u.WelcomeBackAwayFrom, u.WelcomeBackReturnedAt, u.Timezone })
            .FirstOrDefaultAsync(ct);

        if (user is not { WelcomeBackAwayFrom: { } awayFrom, WelcomeBackReturnedAt: { } returnedAt }
            || !UserPresence.CardIsOpen(returnedAt, utcNow))
            return null;

        return new Window(
            awayFrom,
            returnedAt,
            LocalDate(awayFrom, user.Timezone),
            LocalDate(returnedAt, user.Timezone),
            TaskPlannerHelper.TodayIn(user.Timezone, utcNow));
    }

    /// <summary>
    /// Open items due before today, whatever the list, oldest first. The same set the overdue banner's "move them
    /// all" picks (<c>RescheduleOverdueTodoListItemEndpoint</c> with no list): an item overdue before the absence
    /// is still part of the pile the user comes back to.
    /// </summary>
    public static IQueryable<TodoListItem> OverdueTodos(AppDbContext db, long userId, Window window) =>
        db.TodoListItems
            .Where(i => i.UserId == userId && !i.IsDone && i.DueDate != null && i.DueDate < window.Today)
            .OrderBy(i => i.DueDate).ThenBy(i => i.Id);

    /// <summary>
    /// Blocks left open from the day the absence began up to yesterday, oldest first. "Open" is the carry-over
    /// banner's definition (<c>GetCarryOverCandidatesPlannerTaskEndpoint</c>): not started or in progress, and not
    /// background. Bounded below by the absence rather than by that banner's seven days, so a fortnight away is
    /// one pile, not a pile plus a remainder.
    /// </summary>
    public static IQueryable<PlannerTask> PastTasks(AppDbContext db, long userId, Window window) =>
        db.PlannerTasks
            .Where(t => t.UserId == userId
                        && !t.IsBackground
                        && (t.Status == PlannerTaskStatus.NotStarted || t.Status == PlannerTaskStatus.InProgress)
                        && t.Calendar.Date >= window.AwayDate
                        && t.Calendar.Date < window.Today)
            .OrderBy(t => t.Calendar.Date).ThenBy(t => t.StartTime).ThenBy(t => t.Id);

    public static IQueryable<Notification> UnreadNotifications(AppDbContext db, long userId) =>
        db.Notifications.Where(n => n.UserId == userId && !n.IsRead);

    /// <summary>
    /// Visible routines whose streak now stands at 0 after a cycle that closed during the absence and missed its
    /// threshold without being frozen or excused (<c>RoutineStreakFreezeService.IsSuccess</c>, cross-multiplied so
    /// it translates). A cycle's <c>PeriodEnd</c> is a UTC date and the window's are local, the same one-day slack
    /// FO1's <c>RoutineAwayRule</c> accepts at a cycle's edges.
    /// </summary>
    public static IQueryable<RoutineTimePeriod> BrokenRoutines(AppDbContext db, long userId, Window window) =>
        db.RoutineTimePeriods
            .Where(p => p.UserId == userId
                        && !p.IsHidden
                        && p.Streak == 0
                        && p.CompletionHistoryColl.Any(c => c.PeriodEnd > window.AwayDate
                                                            && c.PeriodEnd <= window.Today
                                                            && !c.IsFrozen
                                                            && !c.IsExcused
                                                            && c.TotalCount > 0
                                                            && c.CompletedCount * 100 < p.StreakThreshold * c.TotalCount))
            .OrderBy(p => p.Text);

    private static DateOnly LocalDate(DateTime utc, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(WallClockZone.FromUtc(utc, zone));
}
