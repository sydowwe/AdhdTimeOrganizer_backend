using Microsoft.Extensions.Caching.Memory;

namespace AdhdTimeOrganizer.TodoLists.application.service;

/// <summary>
/// What <c>POST /todo-list-item/reschedule-overdue</c> changed, kept for <see cref="Lifetime"/> so
/// <c>POST /todo-list-item/reschedule-overdue/undo</c> can put it back — the slip record included.
/// <para>
/// The client used to undo the banner by re-sending each item's old body through the PUT. That restored the dates
/// and left every move counted, since a due date pulled earlier is never a postponement: one undone renegotiation
/// still nudged its items toward the stuck-item offer. The before-image has to live here because the client never
/// sees <c>PostponeCount</c> or <c>FirstDueDate</c> (neither reaches the wire, by design).
/// </para>
/// <para>
/// <b>In memory, on purpose,</b> exactly like the welcome-back fresh start's <c>FreshStartUndo</c>: an undo is a
/// snackbar's worth of time, and a restart losing it costs one undo, not a table. The token is random and bound to
/// the user it was issued to. <b>Undo never overwrites a later edit:</b> only an item still open and still on the
/// date the reschedule gave it is restored.
/// </para>
/// </summary>
public static class RescheduleOverdueUndo
{
    /// <summary>How long a renegotiation can be taken back. The client's undo snackbar is far shorter.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public sealed record ItemBefore(long Id, DateOnly? DueDate, DateOnly? FirstDueDate, int PostponeCount);

    /// <param name="DueDateAfter">The date every item was moved to; an item no longer on it was edited since.</param>
    public sealed record Snapshot(long UserId, DateOnly DueDateAfter, IReadOnlyList<ItemBefore> Items);

    public static string Store(IMemoryCache cache, Snapshot snapshot)
    {
        var token = Guid.NewGuid().ToString("N");
        cache.Set(Key(token), snapshot, Lifetime);
        return token;
    }

    /// <summary>The snapshot behind <paramref name="token"/>, if it exists, has not expired and is <paramref name="userId"/>'s.</summary>
    public static Snapshot? Find(IMemoryCache cache, string token, long userId) =>
        cache.TryGetValue(Key(token), out Snapshot? snapshot) && snapshot!.UserId == userId ? snapshot : null;

    /// <summary>Spends the token, so a second undo of the same renegotiation is a 404 rather than a second restore.</summary>
    public static void Forget(IMemoryCache cache, string token) => cache.Remove(Key(token));

    private static string Key(string token) => $"fo3-reschedule-overdue-undo:{token}";
}
