namespace AdhdTimeOrganizer.infrastructure.persistence.interceptors;

/// <summary>
/// Coalesces suggestion-pattern view refresh requests raised by <see cref="SuggestionPatternRefreshInterceptor"/>
/// across many saves into a set that <see cref="AdhdTimeOrganizer.infrastructure.jobs.SuggestionPatternRefreshJob"/>
/// drains and refreshes off the request thread. Marking a view dirty multiple times before the job runs still
/// costs exactly one refresh.
/// </summary>
public interface ISuggestionPatternRefreshQueue
{
    void MarkDirty(SuggestionPatternView view);

    /// <summary>
    /// Atomically returns every currently-dirty view and clears them. The first call on a new UTC date also
    /// reports the two windowed pattern views, written to or not (FO11).
    /// </summary>
    IReadOnlyCollection<SuggestionPatternView> DrainDirty();
}
