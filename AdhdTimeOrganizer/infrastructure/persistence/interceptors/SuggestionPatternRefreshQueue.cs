using System.Collections.Concurrent;

namespace AdhdTimeOrganizer.infrastructure.persistence.interceptors;

/// <summary>Singleton — the dirty set must be shared across every scoped <see cref="AppDbContext"/>/interceptor instance.</summary>
public sealed class SuggestionPatternRefreshQueue : ISuggestionPatternRefreshQueue
{
    private readonly ConcurrentDictionary<SuggestionPatternView, byte> _dirty = new();

    public void MarkDirty(SuggestionPatternView view) => _dirty[view] = 0;

    public IReadOnlyCollection<SuggestionPatternView> DrainDirty()
    {
        var views = _dirty.Keys.ToList();
        foreach (var view in views)
            _dirty.TryRemove(view, out _);
        return views;
    }
}
