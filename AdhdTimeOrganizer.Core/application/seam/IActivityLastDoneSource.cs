using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.seam;

/// <summary>
/// What one source knows about whether the user actually did an activity: the latest positive evidence, and
/// the planned times they did not.
/// </summary>
/// <param name="LastDoneAt">
/// The latest UTC instant this source can show the activity happened at, or <c>null</c>. <b>Positive evidence
/// only</b>: a recording, a completed block, a memory anchor. Absence is weak evidence — most rest is never
/// logged, and an untouched rest block may simply not have been ticked — so nothing here is ever inferred from
/// what is missing.
/// </param>
/// <param name="UnkeptAt">
/// UTC instants of planned times for the activity that passed untouched (Planning only; empty everywhere
/// else). The consumer counts only those after the merged <see cref="LastDoneAt"/>, so a miss the user later
/// made up for does not count against the activity.
/// </param>
public sealed record ActivityDoneEvidence(DateTime? LastDoneAt, IReadOnlyList<DateTime> UnkeptAt)
{
    public static readonly IReadOnlyList<DateTime> NoneUnkept = [];
}

/// <summary>
/// F3-06: "when did the user last actually do this?" across slices that each hold a piece of the answer —
/// History's recordings, Planning's completed (and untouched) blocks, ActivityProfiles' memory anchors. The
/// leisure draw reads it so something done yesterday is not offered tonight, and the card can say "last time:
/// 3 weeks ago".
/// </summary>
/// <remarks>
/// <para>
/// Same shape as <see cref="IActivityReferenceSource"/>: Core declares it and the keys, each owning slice
/// implements it in its own <c>application/seam/</c>, and the consumer resolves the keyed
/// <c>IEnumerable&lt;&gt;</c>. <b>The join is by <c>ActivityId</c> and time only</b> — no interval overlap, no
/// item links — because "did this activity happen" is a question about the activity.
/// </para>
/// <para>
/// ⚠ <b>Fails silently, in the quiet direction.</b> A source dropped from
/// <c>ModuleServiceExtensions.ModuleAssemblies</c> or a renamed key simply contributes nothing, and the draw
/// goes back to staleness from "last shown" alone — a valid, plausible draw. <c>SeamWiringTests</c> pins key
/// coverage, and <c>LeisureLastDoneTests</c> asserts each source's evidence on the card it reaches.
/// </para>
/// <para>
/// Implementations read with the <see cref="DbContext"/> handed in, scope on <paramref name="userId"/> by hand
/// (the caller passes the id it authenticated), and never save.
/// </para>
/// </remarks>
public interface IActivityLastDoneSource : ISeam
{
    /// <summary>An <see cref="ActivityLastDoneSourceKeys"/> constant.</summary>
    string Key { get; }

    /// <summary>
    /// Evidence per activity, for the ids this source has anything to say about. An activity it knows nothing
    /// of is simply absent from the result.
    /// </summary>
    /// <param name="unkeptSinceUtc">Older untouched plans than this have expired and are not returned.</param>
    /// <param name="utcNow">The reference instant; nothing at or after it counts as done or as missed.</param>
    Task<IReadOnlyDictionary<long, ActivityDoneEvidence>> ReadAsync(
        DbContext db, long userId, IReadOnlyCollection<long> activityIds, DateTime unkeptSinceUtc, DateTime utcNow,
        CancellationToken ct);
}
