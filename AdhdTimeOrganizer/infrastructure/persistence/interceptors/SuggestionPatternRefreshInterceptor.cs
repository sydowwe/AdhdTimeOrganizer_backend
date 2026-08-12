using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AdhdTimeOrganizer.infrastructure.persistence.interceptors;

/// <summary>
/// Detects saves touching <see cref="PlannerTask"/>/<see cref="ActivityHistory"/>/<see cref="Calendar"/> and marks
/// the matching suggestion-pattern view dirty in <see cref="ISuggestionPatternRefreshQueue"/>. Deliberately does
/// <b>not</b> run the `REFRESH MATERIALIZED VIEW CONCURRENTLY` itself — that used to happen synchronously here on
/// every save, which paid full-view-scan cost on the request thread and serialized concurrent saves against each
/// other's refresh lock. <see cref="AdhdTimeOrganizer.infrastructure.jobs.SuggestionPatternRefreshJob"/> owns the
/// actual refresh, off the request path and coalesced across whatever saved in between its runs.
/// </summary>
public class SuggestionPatternRefreshInterceptor(ISuggestionPatternRefreshQueue queue) : SaveChangesInterceptor
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

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
    {
        // Only mark dirty once the save actually committed - marking on a save that later throws would
        // queue a refresh for a change that never happened.
        if (_refreshPlanner)
            queue.MarkDirty(SuggestionPatternView.PlannerTask);
        if (_refreshHistory)
            queue.MarkDirty(SuggestionPatternView.ActivityHistory);
        if (_refreshTemplate)
            queue.MarkDirty(SuggestionPatternView.TemplateSuggestion);

        ResetFlags();
        return ValueTask.FromResult(result);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken ct = default)
    {
        // Without this, a save that throws after SavingChangesAsync ran leaves the flags primed - the next
        // successful save on this scoped context, even one touching none of the three types, would still
        // queue a spurious refresh.
        ResetFlags();
        return Task.CompletedTask;
    }

    private void ResetFlags()
    {
        _refreshPlanner = false;
        _refreshHistory = false;
        _refreshTemplate = false;
    }
}
