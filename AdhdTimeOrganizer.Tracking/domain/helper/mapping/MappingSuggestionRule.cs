namespace AdhdTimeOrganizer.Tracking.domain.helper.mapping;

/// <summary>
/// When an uncovered process or app has earned a mapping suggestion (FO9, F3-02): the thresholds and the arithmetic,
/// kept free of I/O so they read in one place. Desktop and android share it; the <see cref="Observation.Key"/> is the
/// process name or the package name, whichever the suggested exact rule would match.
/// </summary>
/// <remarks>
/// <para>
/// <b>Quiet rather than wrong.</b> A rule re-attributes every future record and feeds the completion automation,
/// and nothing announces a bad one, so a suggestion needs a pattern, not an anecdote: at least
/// <see cref="MinSessions"/> separate timer sittings, at least <see cref="MinSeconds"/> of active time inside them,
/// and at least <see cref="MinShare"/> of that key's in-timer time under one activity. Timer sessions are young
/// (they exist since C5), so early on this says nothing, which is the intended failure.
/// </para>
/// <para>
/// <b>A split process is never suggested.</b> A browser used during <i>Thesis</i> and <i>Email</i> timers alike
/// falls under the share threshold for both, and that is correct: a process-level rule would mislabel half of it.
/// Role-level suggestions for that case were considered and left out — a role-only rule moves dashboards but not
/// completion, which is not what a user accepting "Suggested: Thesis" would expect.
/// </para>
/// <para>
/// <b>The phone also has to fill the timer.</b> On the desktop the foreground process during a focus timer is
/// usually the work; on a phone it is as often the distraction from it, and Instagram checked through every Thesis
/// pomodoro passes the three thresholds above. So the android caller passes each sitting's focus length, and the
/// app must then cover at least <see cref="MinFill"/> of the sittings it appeared in — Duolingo through a language
/// timer does, a glance at a feed does not. The desktop does not pass it: work there is spread over an editor, a
/// browser and a viewer, and each would fall short.
/// </para>
/// </remarks>
public static class MappingSuggestionRule
{
    public const int MinSessions = 3;
    public const int MinSeconds = 10 * 60;
    public const double MinShare = 0.7;
    public const double MinFill = 0.5;

    /// <summary>Active seconds of one uncovered process or app observed inside one timer focus interval.</summary>
    public readonly record struct Observation(string Key, string Label, long ActivityId, long SessionId, int ActiveSeconds);

    public sealed record Suggestion(string Key, string Label, long ActivityId, int SessionCount, int Seconds);

    /// <summary>One suggestion per key that meets every threshold, busiest first.</summary>
    /// <param name="observations">The in-timer time of keys no active rule covers.</param>
    /// <param name="sittingSeconds">
    /// Focus seconds per timer session, when the <see cref="MinFill"/> threshold applies (android); null skips it.
    /// </param>
    public static List<Suggestion> Suggest(IEnumerable<Observation> observations, IReadOnlyDictionary<long, int>? sittingSeconds = null) =>
        observations
            .Where(o => o.ActiveSeconds > 0)
            .GroupBy(o => o.Key)
            .Select(key =>
            {
                var total = key.Sum(o => o.ActiveSeconds);
                var best = key
                    .GroupBy(o => o.ActivityId)
                    .Select(g => (ActivityId: g.Key, Seconds: g.Sum(o => o.ActiveSeconds), SessionIds: g.Select(o => o.SessionId).Distinct().ToList()))
                    .OrderByDescending(a => a.Seconds)
                    .ThenBy(a => a.ActivityId)
                    .First();
                // The busiest label names the row; one executable reporting two product names is an edge, not a split.
                var label = key.GroupBy(o => o.Label).MaxBy(g => g.Sum(o => o.ActiveSeconds))!.Key;

                var fills = sittingSeconds is null
                    || best.Seconds >= MinFill * best.SessionIds.Sum(id => sittingSeconds.GetValueOrDefault(id));

                return best.SessionIds.Count >= MinSessions && best.Seconds >= MinSeconds && best.Seconds >= MinShare * total && fills
                    ? new Suggestion(key.Key, label, best.ActivityId, best.SessionIds.Count, best.Seconds)
                    : null;
            })
            .OfType<Suggestion>()
            .OrderByDescending(s => s.Seconds)
            .ThenBy(s => s.Key, StringComparer.Ordinal)
            .ToList();
}
