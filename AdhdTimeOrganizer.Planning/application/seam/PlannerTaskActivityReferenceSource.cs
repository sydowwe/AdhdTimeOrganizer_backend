using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Planning.application.seam;

/// <summary>
/// Publishes all three planner task shapes — one-off, repeating and day-template — through Core's
/// reference seam.
/// </summary>
/// <remarks>
/// <para>
/// Three tables, not one: <c>PlannerTask</c>, <c>RepeatingPlannerTask</c> and
/// <c>TemplatePlannerTask</c> share <c>BasePlannerTask</c> in C# but each has its own configuration and
/// its own table, so each needs its own clause here. Adding a fourth task shape and forgetting this file
/// is a silent undercount and a merge that strands rows — nothing fails to build.
/// </para>
/// <para>
/// The template case is the one worth naming. A day template is a plan the user applies repeatedly, so a
/// template task stranded on a deleted activity keeps producing broken days long after the activity went
/// away — and <c>TemplatePlannerTask.Color</c> reads straight through <c>Activity.Role</c>, so it would
/// throw rather than render blank.
/// </para>
/// <para>
/// The two <c>PlannerSuggestionFrom*</c> read-models are deliberately absent. They sit over materialized
/// views derived from history and planner rows that are already counted here and in History, so counting
/// them would double-count the same underlying fact, and repointing a view is not a thing that can be
/// done. The next refresh reflects the merge on its own.
/// </para>
/// </remarks>
public sealed class PlannerTaskActivityReferenceSource : IActivityReferenceSource, IScopedService
{
    public string Key => ActivityReferenceSourceKeys.PlannerTask;

    public IQueryable<long> ReferencingActivityIds(DbContext db) =>
        db.Set<PlannerTask>().Select(t => t.ActivityId)
            .Concat(db.Set<RepeatingPlannerTask>().Select(t => t.ActivityId))
            .Concat(db.Set<TemplatePlannerTask>().Select(t => t.ActivityId));

    public async Task<int> RepointAsync(DbContext db, long survivorId, IReadOnlyCollection<long> mergedIds, CancellationToken ct)
    {
        return await RepointSetAsync<PlannerTask>(db, survivorId, mergedIds, ct)
               + await RepointSetAsync<RepeatingPlannerTask>(db, survivorId, mergedIds, ct)
               + await RepointSetAsync<TemplatePlannerTask>(db, survivorId, mergedIds, ct);
    }

    private static async Task<int> RepointSetAsync<TTask>(DbContext db, long survivorId, IReadOnlyCollection<long> mergedIds, CancellationToken ct)
        where TTask : BasePlannerTask
    {
        var rows = await db.Set<TTask>()
            .Where(t => mergedIds.Contains(t.ActivityId))
            .ToListAsync(ct);

        foreach (var row in rows)
            row.ActivityId = survivorId;

        return rows.Count;
    }
}
