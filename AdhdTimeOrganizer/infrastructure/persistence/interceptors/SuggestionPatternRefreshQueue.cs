using System.Collections.Concurrent;

namespace AdhdTimeOrganizer.infrastructure.persistence.interceptors;

/// <summary>Singleton — the dirty set must be shared across every scoped <see cref="AppDbContext"/>/interceptor instance.</summary>
/// <remarks>
/// <b>Also dirty once a day, write or no write (FO11).</b> The two pattern views count only a recent window
/// measured from <c>current_date</c> at refresh time, so a view nobody's save has touched since yesterday is
/// a day stale — a quiet week would freeze a pattern the user has already let lapse. The first drain on a new
/// UTC date therefore reports both as dirty, which on a fresh boot also means one refresh straight away.
/// </remarks>
public sealed class SuggestionPatternRefreshQueue(TimeProvider timeProvider) : ISuggestionPatternRefreshQueue
{
    private static readonly SuggestionPatternView[] WindowedViews =
        [SuggestionPatternView.PlannerTask, SuggestionPatternView.ActivityHistory];

    private readonly ConcurrentDictionary<SuggestionPatternView, byte> _dirty = new();
    private DateOnly? _lastDailyMark;

    public void MarkDirty(SuggestionPatternView view) => _dirty[view] = 0;

    public IReadOnlyCollection<SuggestionPatternView> DrainDirty()
    {
        // DisallowConcurrent on the job means one drainer at a time; a racing reader would at worst mark twice,
        // which still costs one refresh.
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (_lastDailyMark != today)
        {
            _lastDailyMark = today;
            foreach (var view in WindowedViews)
                MarkDirty(view);
        }

        var views = _dirty.Keys.ToList();
        foreach (var view in views)
            _dirty.TryRemove(view, out _);
        return views;
    }
}
