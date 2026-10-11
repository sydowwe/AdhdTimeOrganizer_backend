using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.Planning.domain.service;
using AdhdTimeOrganizer.Routines.domain.service;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Contracts.notification.payload;

namespace AdhdTimeOrganizer.application.service.morningBriefing;

/// <summary>
/// What the morning briefing says: the counts for each user's today, composed at send time so a block added at
/// 07:55 is in an 08:00 briefing and a to-do ticked at 07:55 is not.
/// <para>
/// <b>Host-side, reading three slices directly</b>, for the reason <c>WelcomeBackPile</c> gives: the answer spans
/// Planning, TodoLists and Routines, the host already references all of them and owns <c>AppDbContext</c>, and a
/// keyed seam per slice would be three string-keyed registrations that fail silently, for a read nothing else
/// performs. The rules stay in their slices and are called, not copied: <see cref="PlannerTaskCarryOverRule"/> for
/// what is open and what is carried over, <see cref="RoutineSuggestedDay.IsOpenSuggestionOn"/> for the routine
/// items — the very rule the routine slice's own digest reads.
/// </para>
/// <para>
/// <b>Bulk, one query per section for a whole page of users</b>, because it runs inside a quarter-hourly sweep.
/// Every query scopes by <c>UserId</c> by hand; the sweep is unauthenticated, so the global user filter is inert.
/// </para>
/// </summary>
public static class MorningBriefingComposer
{
    /// <param name="Today">The user's own date.</param>
    /// <param name="LocalNow">The user's wall clock now — the first block counted as "first" starts at or after it.</param>
    public sealed record Morning(DateOnly Today, DateTime LocalNow);

    /// <summary>One payload per user in <paramref name="mornings"/>, every section counted — including the all-zero ones.</summary>
    public static async Task<Dictionary<long, MorningBriefingPayload>> ComposeAsync(
        AppDbContext db, IReadOnlyDictionary<long, Morning> mornings, DateTime utcNow, CancellationToken ct)
    {
        if (mornings.Count == 0)
            return [];

        var userIds = mornings.Keys.ToList();
        var earliest = mornings.Values.Min(m => m.Today);
        var latest = mornings.Values.Max(m => m.Today);

        // Today's blocks and the carry-over window in one read: both are "open foreground" blocks, only the day differs.
        var blocks = await db.PlannerTasks
            .AsNoTracking()
            .Where(PlannerTaskCarryOverRule.IsOpen)
            .Where(t => userIds.Contains(t.UserId)
                        && t.Calendar.Date >= earliest.AddDays(-PlannerTaskCarryOverRule.LookbackDays)
                        && t.Calendar.Date <= latest)
            .Select(t => new { t.UserId, t.Calendar.Date, t.StartTime })
            .ToListAsync(ct);

        // Counted in SQL by due date: a long overdue backlog is one row per date, not one per item.
        var todos = await db.TodoListItems
            .AsNoTracking()
            .Where(i => userIds.Contains(i.UserId) && !i.IsDone && i.DueDate != null && i.DueDate <= latest)
            .GroupBy(i => new { i.UserId, DueDate = i.DueDate!.Value, Untimed = i.DueTime == null })
            .Select(g => new { g.Key.UserId, g.Key.DueDate, g.Key.Untimed, Count = g.Count() })
            .ToListAsync(ct);

        // Daily periods never suggest a day (RoutineSuggestedDay), so they are not loaded at all.
        var periods = await db.RoutineTimePeriods
            .AsNoTracking()
            .Where(p => userIds.Contains(p.UserId) && !p.IsHidden && p.LengthInDays > 1)
            .Include(p => p.RoutineTodoListColl)
            .ToListAsync(ct);

        var result = new Dictionary<long, MorningBriefingPayload>(mornings.Count);
        foreach (var (userId, morning) in mornings)
        {
            var today = morning.Today;
            var userBlocks = blocks.Where(b => b.UserId == userId).ToList();
            var todays = userBlocks.Where(b => b.Date == today).ToList();
            var nowTime = TimeOnly.FromDateTime(morning.LocalNow);
            var userTodos = todos.Where(t => t.UserId == userId).ToList();

            result[userId] = new MorningBriefingPayload(
                today,
                Blocks: todays.Count,
                FirstBlockAt: todays.Where(b => b.StartTime >= nowTime).Select(b => (TimeOnly?)b.StartTime).Min(),
                CarryOver: userBlocks.Count(b => PlannerTaskCarryOverRule.IsWithinLookback(b.Date, today)),
                // Date-only: a to-do with a due time has its own reminder at that time (TodoItemDue).
                DueToday: userTodos.Where(t => t.DueDate == today && t.Untimed).Sum(t => t.Count),
                Overdue: userTodos.Where(t => t.DueDate < today).Sum(t => t.Count),
                RoutinesSuggested: periods
                    .Where(p => p.UserId == userId)
                    .Sum(p => p.RoutineTodoListColl.Count(i => RoutineSuggestedDay.IsOpenSuggestionOn(p, i, today, utcNow))));
        }

        return result;
    }

    /// <summary>
    /// Nothing to say: no plan, nothing carried over, due or overdue, no routine suggested. Such a morning gets no
    /// briefing at all — an identical "nothing today" push every morning is the one users learn to ignore.
    /// </summary>
    public static bool IsEmpty(MorningBriefingPayload payload) =>
        payload is { Blocks: <= 0, CarryOver: <= 0, DueToday: <= 0, Overdue: <= 0, RoutinesSuggested: <= 0 };
}
