using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.android;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Tracking.application.seam;

/// <summary>
/// Publishes the desktop and android pattern→activity mappings through Core's reference seam.
/// </summary>
/// <remarks>
/// <para>
/// These are rules rather than records, and that is what makes them worth counting: a mapping is how
/// tracked time <em>keeps</em> arriving. Lose it and every future heartbeat matching that pattern
/// silently stops being attributed — the user sees no error, just time that stops appearing.
/// </para>
/// <para>
/// Both FKs are ordinary many-to-one, optional, <c>Cascade</c> references — in line with every other
/// activity reference in the solution, and pinned by <c>ActivityForeignKeyInventoryTests</c>. So every
/// merged mapping is simply repointed onto the survivor and none is deleted: an activity may be the
/// target of any number of pattern rules, because the domain key is the pattern (its own composite
/// unique index per user), not the activity.
/// </para>
/// <para>
/// Note that repointing is the <em>only</em> option here even where it looks like blanking would do:
/// the <c>CK_Tracker*MappingByPattern_TargetRequired</c> check constraint requires <em>exactly one</em>
/// of "ignored", "an activity", or "a role/category", so a mapping whose <c>ActivityId</c> was nulled
/// fails the constraint rather than surviving as an unmapped rule.
/// </para>
/// <para>
/// <c>RoleId</c> / <c>CategoryId</c> on these mappings are a separate fallback rule, not a reference to
/// the merged activity's placement, and are left alone: the survivor's role wins for the activity, but a
/// mapping's own fallback is the user's setting for that pattern. The three raw ingest ledgers hold no
/// activity FK at all — attribution happens through these mappings — so nothing here reaches into
/// <c>WebExtensionActivityEntry</c>, the one entity outside the global user filter.
/// </para>
/// </remarks>
public sealed class TrackerMappingActivityReferenceSource : IActivityReferenceSource, IScopedService
{
    public string Key => ActivityReferenceSourceKeys.TrackerMapping;

    public IQueryable<long> ReferencingActivityIds(DbContext db) =>
        db.Set<TrackerDesktopMappingByPattern>()
            .Where(m => m.ActivityId != null)
            .Select(m => m.ActivityId!.Value)
            .Concat(db.Set<TrackerAndroidMappingByPattern>()
                .Where(m => m.ActivityId != null)
                .Select(m => m.ActivityId!.Value));

    public async Task<int> RepointAsync(DbContext db, long survivorId, IReadOnlyCollection<long> mergedIds, CancellationToken ct)
    {
        var desktop = await db.Set<TrackerDesktopMappingByPattern>()
            .Where(m => m.ActivityId != null && mergedIds.Contains(m.ActivityId.Value))
            .ToListAsync(ct);

        foreach (var mapping in desktop)
            mapping.ActivityId = survivorId;

        var android = await db.Set<TrackerAndroidMappingByPattern>()
            .Where(m => m.ActivityId != null && mergedIds.Contains(m.ActivityId.Value))
            .ToListAsync(ct);

        foreach (var mapping in android)
            mapping.ActivityId = survivorId;

        return desktop.Count + android.Count;
    }
}
