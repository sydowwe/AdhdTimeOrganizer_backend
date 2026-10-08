using AdhdTimeOrganizer.Planning.domain.model.@enum;
using Microsoft.Extensions.Caching.Memory;

namespace AdhdTimeOrganizer.application.service.welcomeBack;

/// <summary>
/// FO5: what a fresh start changed, kept for <see cref="Lifetime"/> so <c>POST /welcome-back/fresh-start/undo</c> can
/// put it back. The client undoes its other bulk actions itself, by re-sending each row's old state; a fresh start
/// changes rows in four slices the client never loaded, so the before-image has to live here.
/// <para>
/// <b>In memory, on purpose.</b> An undo is a snackbar's worth of time, and a restart losing it costs the user one
/// undo, which is the right price for not adding a table. The token is random and bound to the user it was issued
/// to, so it cannot be replayed against another account.
/// </para>
/// <para>
/// <b>Undo never overwrites a later edit.</b> Each row records the state the fresh start left it in, and the undo
/// restores only rows still in that state. An item ticked off or a task dragged in the meantime stays as the user
/// left it.
/// </para>
/// </summary>
public static class FreshStartUndo
{
    /// <summary>How long a fresh start can be taken back.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public sealed record TodoBefore(
        long Id, DateOnly? DueDate, TimeOnly? DueTime, DateOnly? FirstDueDate, int PostponeCount,
        DateOnly? DueDateAfter);

    public sealed record TaskBefore(
        long Id, long CalendarId, PlannerTaskStatus Status, string? SkipReason, SkipReasonKey? SkipReasonKey,
        TimeOnly? ActualStartTime, TimeOnly? ActualEndTime, DateOnly? OriginalDate, TimeOnly? OriginalStartTime,
        int CarryOverCount, long CalendarIdAfter, PlannerTaskStatus StatusAfter);

    /// <param name="AwayFrom">The card's absence, so the undo can reopen it.</param>
    /// <param name="ReturnedAt">Likewise.</param>
    public sealed record Snapshot(
        long UserId,
        DateTime AwayFrom,
        DateTime ReturnedAt,
        IReadOnlyList<TodoBefore> Todos,
        IReadOnlyList<TaskBefore> Tasks,
        IReadOnlyList<long> NotificationIds);

    public static string Store(IMemoryCache cache, Snapshot snapshot)
    {
        var token = Guid.NewGuid().ToString("N");
        cache.Set(Key(token), snapshot, Lifetime);
        return token;
    }

    /// <summary>The snapshot behind <paramref name="token"/>, if it exists, has not expired and is <paramref name="userId"/>'s.</summary>
    public static Snapshot? Find(IMemoryCache cache, string token, long userId) =>
        cache.TryGetValue(Key(token), out Snapshot? snapshot) && snapshot!.UserId == userId ? snapshot : null;

    /// <summary>Spends the token, so a second undo of the same fresh start is a 404 rather than a second restore.</summary>
    public static void Forget(IMemoryCache cache, string token) => cache.Remove(Key(token));

    private static string Key(string token) => $"fo5-fresh-start-undo:{token}";
}
