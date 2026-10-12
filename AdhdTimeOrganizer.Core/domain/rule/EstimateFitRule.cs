namespace AdhdTimeOrganizer.Core.domain.rule;

/// <summary>
/// The one rule every "how long things really take" read follows (FO8): a <b>median</b>, and <b>nothing at all</b>
/// below a minimum number of samples.
/// </summary>
/// <remarks>
/// <para>
/// In Core because its three readers sit in three slices that cannot see each other — to-do items (TodoLists,
/// F3-01), routine items (Routines, F2-06) and timer lengths (History, F2-07). Each keeps its own threshold here.
/// The two estimate reads take the median from here, so their figures can be read side by side. The timer read
/// asks a different question — which length is usually <em>reached</em> — and keeps its share here instead.
/// </para>
/// <para>
/// <b>A median, not a mean.</b> The samples are ratios, and one task that ran six times its estimate would drag a
/// mean onto a number no ordinary task came near. <b>Silence, not a small sample.</b> A figure built from two
/// items reads as a fact when it is closer to noise, so below the threshold a reader returns nothing rather than
/// a hedged number.
/// </para>
/// <para>
/// The figure describes the <b>estimate</b>, never the person, and it is never written back: <c>SuggestedTime</c>
/// also decides when the tracker completes an item, so rewriting it from this would move that line silently.
/// </para>
/// </remarks>
public static class EstimateFitRule
{
    /// <summary>
    /// Completed to-do items per group before the to-do read says anything (F3-01). The same five the activity-keyed
    /// calibration it replaces used.
    /// </summary>
    public const int TodoItemGroupMinimumSamples = 5;

    /// <summary>
    /// Timed occurrences of one routine item before its typical length is shown (F2-06). Lower than the to-do
    /// threshold because the figure describes one item repeating itself, not a group of different tasks, and a
    /// weekly item would otherwise need five weeks of timing before it said anything.
    /// </summary>
    public const int RoutineItemMinimumSamples = 3;

    /// <summary>
    /// Timer sessions of one activity before a focus length is suggested (F2-07), and also the focus stretches that
    /// must bear on the suggested length itself. Five, so one long Pomodoro cannot carry it alone.
    /// </summary>
    public const int TimerLengthMinimumSamples = 5;

    /// <summary>
    /// The share of focus stretches that must reach a length before it is suggested (F2-07): the largest length
    /// reached at least this often is the one offered.
    /// </summary>
    public const double TimerLengthReachedShare = 0.7;

    /// <summary>
    /// The median of <paramref name="samples"/>, or <c>null</c> when there are fewer than
    /// <paramref name="minimumSamples"/>. An even count takes the mean of the two middle values.
    /// </summary>
    public static double? MedianOrSilent(IReadOnlyCollection<double> samples, int minimumSamples)
    {
        if (samples.Count == 0 || samples.Count < minimumSamples)
            return null;

        var sorted = samples.Order().ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}
