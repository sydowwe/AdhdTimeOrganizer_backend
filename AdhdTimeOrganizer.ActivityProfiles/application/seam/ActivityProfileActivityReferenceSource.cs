using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.application.seam;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.entityInterface;

namespace AdhdTimeOrganizer.ActivityProfiles.application.seam;

/// <summary>
/// Publishes this slice's five activity references — the three 1:1 profiles, memory anchors and the
/// leisure draw history — through Core's reference seam.
/// </summary>
/// <remarks>
/// <para>
/// This is the slice with the real collision rules, which is the whole reason repointing is a seam
/// method the owning slice implements rather than a generic FK rewrite Core could do on its own.
/// </para>
/// <para>
/// <b>The three profiles are unique per activity.</b> Merging two activities that each carry a backlog
/// profile cannot produce an activity with two of them. The survivor's profile wins and the merged one
/// is deleted — the survivor is the row the user chose to keep, and its profile is the one they have
/// been curating. Where the survivor has none, the merged profile is repointed and survives, so folding
/// a fully-described duplicate into a bare survivor keeps the description rather than discarding it.
/// A profile deleted to resolve the collision still counts as repointed; see the seam's remarks.
/// </para>
/// <para>
/// <b><c>LeisureSuggestionRecord</c> is unique per (user, source, activity)</b> — the draw's memory. Two
/// records for the same source collapse to the one with the later <c>LastSuggestedAt</c>, because the
/// question it answers is "have I shown this recently", and the more recent answer is the correct one.
/// </para>
/// <para>
/// <b><c>MemoryAnchor</c> has no uniqueness rule</b> — its index on
/// (activity, year, month) is non-unique, and several highlights in one month is the intended shape.
/// Everything repoints; nothing collapses.
/// </para>
/// <para>
/// ⚠ These are the entities with <b>no global user query filter</b> — the profiles are
/// <c>BaseTableEntity</c>, not <c>BaseEntityWithUser</c>. Neither method here scopes by hand, and that
/// is safe rather than an oversight: both are keyed entirely by activity id, and the ids they are
/// matched against have already come through a user-filtered read in Core (the grid's outer query, the
/// merge endpoint's survivor/merged lookups). A profile belonging to another user hangs off another
/// user's activity and therefore cannot match. Any future method here that filters on something other
/// than an activity id does <b>not</b> inherit that guarantee.
/// </para>
/// </remarks>
public sealed class ActivityProfileActivityReferenceSource : IActivityReferenceSource, IScopedService
{
    public string Key => ActivityReferenceSourceKeys.ActivityProfile;

    public IQueryable<long> ReferencingActivityIds(DbContext db) =>
        db.Set<ActivityBacklogProfile>().Select(p => p.ActivityId)
            .Concat(db.Set<ActivityProjectProfile>().Select(p => p.ActivityId))
            .Concat(db.Set<ActivityBucketListProfile>().Select(p => p.ActivityId))
            .Concat(db.Set<MemoryAnchor>().Select(a => a.ActivityId))
            .Concat(db.Set<LeisureSuggestionRecord>().Select(r => r.ActivityId));

    public async Task<int> RepointAsync(DbContext db, long survivorId, IReadOnlyCollection<long> mergedIds, CancellationToken ct)
    {
        var repointed = await RepointUniqueProfileAsync<ActivityBacklogProfile>(db, survivorId, mergedIds, ct);
        repointed += await RepointUniqueProfileAsync<ActivityProjectProfile>(db, survivorId, mergedIds, ct);
        repointed += await RepointUniqueProfileAsync<ActivityBucketListProfile>(db, survivorId, mergedIds, ct);

        var anchors = await db.Set<MemoryAnchor>()
            .Where(a => mergedIds.Contains(a.ActivityId))
            .ToListAsync(ct);
        foreach (var anchor in anchors)
            anchor.ActivityId = survivorId;
        repointed += anchors.Count;

        repointed += await RepointSuggestionRecordsAsync(db, survivorId, mergedIds, ct);

        return repointed;
    }

    /// <summary>
    /// Repoints one profile family, honouring its unique <c>ActivityId</c>: at most one row can end up on
    /// the survivor, and any others are deleted.
    /// </summary>
    private static async Task<int> RepointUniqueProfileAsync<TProfile>(DbContext db, long survivorId, IReadOnlyCollection<long> mergedIds, CancellationToken ct)
        where TProfile : class, IActivityProfile, IEntityWithId
    {
        var candidates = await db.Set<TProfile>()
            .Where(p => mergedIds.Contains(p.ActivityId))
            .OrderBy(p => p.Id)
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return 0;

        var survivorHasOne = await db.Set<TProfile>().AnyAsync(p => p.ActivityId == survivorId, ct);

        // Ordered by id and taking the first, so that when the survivor has no profile of this kind and
        // several merged activities do, the oldest one wins — the profile the user has had longest —
        // rather than whichever row Postgres happened to return first.
        var keeper = survivorHasOne ? null : candidates[0];

        foreach (var candidate in candidates)
        {
            if (ReferenceEquals(candidate, keeper))
                candidate.ActivityId = survivorId;
            else
                db.Set<TProfile>().Remove(candidate);
        }

        return candidates.Count;
    }

    /// <summary>
    /// Repoints the leisure draw history, collapsing per (source, activity) to the most recent draw.
    /// </summary>
    private static async Task<int> RepointSuggestionRecordsAsync(DbContext db, long survivorId, IReadOnlyCollection<long> mergedIds, CancellationToken ct)
    {
        var records = await db.Set<LeisureSuggestionRecord>()
            .Where(r => mergedIds.Contains(r.ActivityId))
            .ToListAsync(ct);

        if (records.Count == 0)
            return 0;

        var survivorSources = await db.Set<LeisureSuggestionRecord>()
            .Where(r => r.ActivityId == survivorId)
            .ToListAsync(ct);

        foreach (var group in records.GroupBy(r => r.Source))
        {
            var existing = survivorSources.FirstOrDefault(r => r.Source == group.Key);
            var mostRecent = group.OrderByDescending(r => r.LastSuggestedAt).First();

            if (existing == null)
            {
                mostRecent.ActivityId = survivorId;
            }
            else if (mostRecent.LastSuggestedAt > existing.LastSuggestedAt)
            {
                // The survivor's own record stays the row (its id is what the draw's history is keyed on
                // elsewhere); only the answer it carries is updated to the newer one.
                existing.LastSuggestedAt = mostRecent.LastSuggestedAt;
                existing.LastOutcome = mostRecent.LastOutcome;
            }

            foreach (var record in group.Where(r => r.ActivityId != survivorId))
                db.Set<LeisureSuggestionRecord>().Remove(record);
        }

        return records.Count;
    }
}
