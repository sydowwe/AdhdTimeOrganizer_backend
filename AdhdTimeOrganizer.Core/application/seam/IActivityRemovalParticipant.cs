using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.seam;

/// <summary>
/// Lets a slice act on activities that are about to disappear — deleted outright, or folded into a survivor by a
/// merge — when what it has to do can only happen <b>after</b> Core's commit but needs state that only exists
/// <b>before</b> it. The database cascade carries every dependent row away without passing through its slice, so a
/// slice holding something outside the database for those rows (a registered reminder, say) learns nothing unless
/// it is asked first.
/// <para>
/// Two phases. <see cref="BeforeRemovalAsync"/> runs inside the request, before anything is written, and reads
/// whatever the slice will need — typically the ids of its rows on those activities. It returns a callback, or
/// <c>null</c> when it has nothing to do; Core runs the callback once the removal has committed, and never when it
/// failed. Splitting it this way is the point: an event published after the commit would find the rows already
/// gone, and acting before the commit would act on a delete that might still roll back.
/// </para>
/// <para>
/// Rules: <see cref="BeforeRemovalAsync"/> only reads — it must not write to the <see cref="DbContext"/> it is
/// handed. The callback runs after a commit the request cannot undo, so it must not throw for anything it can
/// handle; Core logs a throw and carries on with the next participant.
/// </para>
/// </summary>
public interface IActivityRemovalParticipant : ISeam
{
    /// <summary>One of <see cref="ActivityRemovalParticipantKeys"/>.</summary>
    string Key { get; }

    /// <summary>
    /// Captures what the slice needs about the rows hanging off <paramref name="activityIds"/>, and returns the
    /// work to do after the removal commits — or <c>null</c> for none.
    /// </summary>
    Task<Func<CancellationToken, Task>?> BeforeRemovalAsync(DbContext db, IReadOnlyCollection<long> activityIds, CancellationToken ct);
}
