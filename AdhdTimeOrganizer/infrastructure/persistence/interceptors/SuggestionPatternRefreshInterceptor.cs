using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AdhdTimeOrganizer.infrastructure.persistence.interceptors;

public class SuggestionPatternRefreshInterceptor : SaveChangesInterceptor
{
    private bool _refreshPlanner;
    private bool _refreshHistory;
    private bool _refreshTemplate;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        var changed = eventData.Context!.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => e.Entity.GetType())
            .ToHashSet();

        _refreshPlanner = changed.Contains(typeof(PlannerTask));
        _refreshHistory = changed.Contains(typeof(ActivityHistory));
        _refreshTemplate = changed.Contains(typeof(Calendar));

        return ValueTask.FromResult(result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
    {
        var db = eventData.Context!.Database;

        if (_refreshPlanner)
            await db.ExecuteSqlRawAsync(
                "REFRESH MATERIALIZED VIEW CONCURRENTLY mv_planner_task_pattern", ct);

        if (_refreshHistory)
            await db.ExecuteSqlRawAsync(
                "REFRESH MATERIALIZED VIEW CONCURRENTLY mv_activity_history_pattern", ct);

        if (_refreshTemplate)
            await db.ExecuteSqlRawAsync(
                "REFRESH MATERIALIZED VIEW CONCURRENTLY mv_template_suggestion_pattern", ct);

        _refreshPlanner = false;
        _refreshHistory = false;
        _refreshTemplate = false;
        return result;
    }
}
