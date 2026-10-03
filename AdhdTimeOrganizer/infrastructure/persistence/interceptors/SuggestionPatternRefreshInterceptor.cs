using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sydowwe.Framework.domain.entity.user;

namespace AdhdTimeOrganizer.infrastructure.persistence.interceptors;

/// <summary>
/// Detects saves touching <see cref="PlannerTask"/>/<see cref="ActivityHistory"/>/<see cref="Calendar"/> (and a
/// user's time zone, which the history view reads) and marks the matching suggestion-pattern view dirty in <see cref="ISuggestionPatternRefreshQueue"/>. Deliberately does
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
        var entries = eventData.Context!.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();
        var changed = entries.Select(e => e.Entity.GetType()).ToHashSet();

        _refreshPlanner = changed.Contains(typeof(PlannerTask));
        _refreshHistory = changed.Contains(typeof(ActivityHistory)) || entries.Any(TimezoneChanged);
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

    /// <summary>
    /// <c>mv_activity_history_pattern</c> takes each sitting's weekday and time of day in the owner's
    /// <c>user.timezone</c>, so moving zones changes the view as surely as a new history row does.
    /// </summary>
    /// <remarks>
    /// Compared by value, not by <c>IsModified</c>: Identity's <c>UserStore.UpdateAsync</c> calls
    /// <c>Context.Update(user)</c>, which marks every column modified, so <c>IsModified</c> would refresh the
    /// view on every theme or locale change too.
    /// </remarks>
    private static bool TimezoneChanged(EntityEntry entry) =>
        entry is { State: EntityState.Modified, Entity: BaseUser }
        && entry.Property(nameof(BaseUser.Timezone)) is var timezone
        && (timezone.OriginalValue as TimeZoneInfo)?.Id != (timezone.CurrentValue as TimeZoneInfo)?.Id;

    private void ResetFlags()
    {
        _refreshPlanner = false;
        _refreshHistory = false;
        _refreshTemplate = false;
    }
}
