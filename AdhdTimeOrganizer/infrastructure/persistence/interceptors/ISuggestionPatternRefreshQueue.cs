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

    /// <summary>Atomically returns every currently-dirty view and clears them.</summary>
    IReadOnlyCollection<SuggestionPatternView> DrainDirty();
}
