using AdhdTimeOrganizer.Core.application.seam;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.domain.serviceContract;

/// <summary>
/// The one face Core's removing endpoints inject over the keyed <see cref="IActivityRemovalParticipant"/>s —
/// resolved singly, like <see cref="IActivityReferenceService"/>, so a missing registration throws at endpoint
/// activation instead of silently skipping every participant.
/// </summary>
public interface IActivityRemovalService
{
    /// <summary>
    /// Asks every participant what it needs before <paramref name="activityIds"/> are removed. Call it before the
    /// first write; run the returned callback after the commit, and only then.
    /// </summary>
    Task<Func<CancellationToken, Task>> BeforeRemovalAsync(DbContext db, IReadOnlyCollection<long> activityIds, CancellationToken ct);
}
