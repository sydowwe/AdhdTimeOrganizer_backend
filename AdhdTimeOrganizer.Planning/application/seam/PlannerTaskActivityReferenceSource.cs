using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.entity.suggestion;
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
/// <para>
/// <b><c>PlannerSuggestionDismissal</c> (FO11) is present</b>, because it is not derived: it is the user's own
/// "not any more", and a merge that left it behind would cascade it away with the merged activity and bring the
/// pattern straight back. It is unique per (user, activity, pattern type, pattern value), so dismissals of the
/// same pattern collapse to one carrying the latest date — the one that holds longest, as the user meant.
/// </para>
/// </remarks>
public sealed class PlannerTaskActivityReferenceSource : IActivityReferenceSource, IScopedService
{
    public string Key => ActivityReferenceSourceKeys.PlannerTask;

    public IQueryable<long> ReferencingActivityIds(DbContext db) =>
        db.Set<PlannerTask>().Select(t => t.ActivityId)
            .Concat(db.Set<RepeatingPlannerTask>().Select(t => t.ActivityId))
            .Concat(db.Set<TemplatePlannerTask>().Select(t => t.ActivityId))
            .Concat(db.Set<PlannerSuggestionDismissal>().Select(d => d.ActivityId));

    public async Task<int> RepointAsync(DbContext db, long survivorId, IReadOnlyCollection<long> mergedIds, CancellationToken ct)
    {
        return await RepointSetAsync<PlannerTask>(db, survivorId, mergedIds, ct)
               + await RepointSetAsync<RepeatingPlannerTask>(db, survivorId, mergedIds, ct)
               + await RepointSetAsync<TemplatePlannerTask>(db, survivorId, mergedIds, ct)
               + await RepointDismissalsAsync(db, survivorId, mergedIds, ct);
    }

    private static async Task<int> RepointDismissalsAsync(DbContext db, long survivorId, IReadOnlyCollection<long> mergedIds, CancellationToken ct)
    {
        var merged = await db.Set<PlannerSuggestionDismissal>()
            .Where(d => mergedIds.Contains(d.ActivityId))
            .ToListAsync(ct);

        if (merged.Count == 0)
            return 0;

        var survivors = await db.Set<PlannerSuggestionDismissal>()
            .Where(d => d.ActivityId == survivorId)
            .ToListAsync(ct);

        foreach (var group in merged.GroupBy(d => (d.PatternType, d.PatternValue)))
        {
            var latest = group.MaxBy(d => d.DismissedOn)!;
            var existing = survivors.FirstOrDefault(d => d.PatternType == group.Key.PatternType && d.PatternValue == group.Key.PatternValue);

            if (existing is null)
            {
                latest.ActivityId = survivorId;
            }
            else if (latest.DismissedOn > existing.DismissedOn)
            {
                existing.DismissedOn = latest.DismissedOn;
            }

            foreach (var dismissal in group.Where(d => d.ActivityId != survivorId))
                db.Set<PlannerSuggestionDismissal>().Remove(dismissal);
        }

        return merged.Count;
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
