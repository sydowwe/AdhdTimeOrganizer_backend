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
/// ⚠ <b>Both mappings are one-to-one with <c>Activity</c>, not many-to-one.</b> Each configuration says
/// <c>HasOne(e =&gt; e.Activity).WithOne()</c>, which gives <c>ActivityId</c> a <b>unique</b> index —
/// an activity is the target of at most one desktop mapping and at most one android mapping. That makes
/// this the second family (after ActivityProfiles' three profiles) where repointing two rows onto one
/// survivor is impossible rather than merely unusual, and it is easy to miss because the entity reads
/// like an ordinary nullable FK.
/// </para>
/// <para>
/// The collision resolution is a delete, and there is no gentler option: the
/// <c>CK_Tracker*MappingByPattern_TargetRequired</c> check constraint requires <em>exactly one</em> of
/// "ignored", "an activity", or "a role/category", so blanking <c>ActivityId</c> to keep the pattern row
/// would make it fail the constraint rather than survive as an unmapped rule. The survivor's own mapping
/// wins where it has one; otherwise the oldest merged mapping is kept and repointed.
/// </para>
/// <para>
/// ⚠ These same two FKs are also the solution's only activity references that are <b>not</b>
/// <c>Cascade</c>. The relationships are optional with no explicit <c>OnDelete</c>, so EF's default for
/// an optional relationship (<c>ClientSetNull</c>) leaves the database constraint at <c>NO ACTION</c> —
/// meaning <c>DELETE /activity/{id}</c> on an activity that carries a mapping <em>fails</em> rather than
/// cascading. Every other referencing table is destroyed by that same delete. Do not restate the blanket
/// "everything cascades" rule without this exception.
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
            .OrderBy(m => m.Id)
            .ToListAsync(ct);
        var desktopKeeper = await db.Set<TrackerDesktopMappingByPattern>().AnyAsync(m => m.ActivityId == survivorId, ct)
            ? null
            : desktop.FirstOrDefault();

        foreach (var mapping in desktop)
        {
            if (ReferenceEquals(mapping, desktopKeeper))
                mapping.ActivityId = survivorId;
            else
                db.Set<TrackerDesktopMappingByPattern>().Remove(mapping);
        }

        var android = await db.Set<TrackerAndroidMappingByPattern>()
            .Where(m => m.ActivityId != null && mergedIds.Contains(m.ActivityId.Value))
            .OrderBy(m => m.Id)
            .ToListAsync(ct);
        var androidKeeper = await db.Set<TrackerAndroidMappingByPattern>().AnyAsync(m => m.ActivityId == survivorId, ct)
            ? null
            : android.FirstOrDefault();

        foreach (var mapping in android)
        {
            if (ReferenceEquals(mapping, androidKeeper))
                mapping.ActivityId = survivorId;
            else
                db.Set<TrackerAndroidMappingByPattern>().Remove(mapping);
        }

        return desktop.Count + android.Count;
    }
}
