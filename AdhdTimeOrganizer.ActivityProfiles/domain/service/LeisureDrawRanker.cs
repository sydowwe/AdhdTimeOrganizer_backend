using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;

namespace AdhdTimeOrganizer.ActivityProfiles.domain.service;

/// <summary>
/// The picker's ranking rule, written down. Pure — no EF, no HTTP, no clock of its own — so the rule can
/// be read, tested and argued with in one place.
///
/// <para><b>Why a rule at all, rather than "show the matches".</b> The three leisure tables already
/// filter. A picker that filtered and listed would reproduce the paralysis it exists to cure (Iyengar
/// &amp; Lepper 2000: bigger choice sets lower both the probability of choosing and satisfaction with the
/// choice). So the draw is capped at a handful of cards, which makes <i>ordering</i> the entire product.
/// Everything below is the ordering.</para>
///
/// <para><b>Hard constraints (<see cref="IsEligible"/>) exclude; they do not penalise.</b>
/// <list type="bullet">
/// <item><b>Backlog</b>: a stated duration longer than the time available, or a <c>MinParticipants</c>
/// above the people available. Cost tier and location type are excluded in SQL before candidates get
/// here — the query can express them, so it should.</item>
/// <item><b>Project</b>: <c>NeedsShopping</c> is not a suggestion, it is an errand. And a project session
/// needs a real block of time — under <see cref="ProjectMinMinutes"/> there is no point starting one.
/// <c>EstimatedHours</c> is deliberately NOT a hard constraint: it estimates the whole project, not one
/// sitting, so excluding a 20-hour build because the user has two hours would be wrong.</item>
/// <item><b>Bucket list</b>: these rows record no duration, cost or party size at all, so the only honest
/// time constraint is a floor — a bucket-list experience is never a 30-minute filler. Travel raises the
/// floor again.</item>
/// </list></para>
///
/// <para><b>Proxy energy.</b> Only the backlog records an energy level. Ranking the other two against the
/// user's stated energy still matters — an Expert project is a bad answer to "I'm exhausted" — so both
/// derive one: difficulty for projects, comfort-zone step for bucket-list entries. Derived, and labelled
/// as derived everywhere it reaches the UI.</para>
///
/// <para><b>Soft signals (<see cref="Score"/>) rank.</b> Energy fit (asymmetric: too demanding is worse
/// than too easy), duration fit (using most of the free time beats a five-minute filler), effort variety
/// against the last thing committed to, staleness (the same three every visit is the failure mode of a
/// top-3), source weight (the bucket list is a rare treat, not the daily driver), project readiness,
/// comfort step, and jitter — because a deterministic top-3 is invisible after a week.</para>
///
/// <para><b>The jitter is derived from the candidate key and the draw seed</b>, not from a sequential RNG:
/// the draw must not depend on the order rows came back in, and reloading a <c>?seed=</c> URL must
/// reproduce the same cards. This is the same FNV-1a + mulberry32 pair the client's rule used, kept
/// bit-compatible on purpose — a draw made before this endpoint existed and one made after should not
/// disagree about what was on the screen.</para>
/// </summary>
public static class LeisureDrawRanker
{
    /// <summary>How many cards the picker shows unless the caller asks for another number. The single most important number in the module.</summary>
    public const int DefaultSuggestionCount = 3;

    /// <summary>Under this, starting a project is not worth the setup.</summary>
    public const int ProjectMinMinutes = 60;

    /// <summary>Under this, a bucket-list experience is not what the user has time for.</summary>
    public const int BucketListMinMinutes = 120;

    /// <summary>A bucket-list entry that requires travel needs most of a day, not an evening.</summary>
    public const int BucketListTravelMinMinutes = 240;

    /// <summary>Never book a slot shorter than this — a five-minute block in the planner is noise, not a plan.</summary>
    public const int MinSlotMinutes = 10;

    /// <summary>
    /// At most this many of a draw may come from one source. Backlog is uncapped on purpose — it is the
    /// pool the picker is really for. The caps shape a normal draw and are relaxed rather than enforced
    /// when the eligible pool is too thin to fill the cards.
    /// </summary>
    private static int SourceCap(LeisureSuggestionSource source, int count) => source switch
    {
        LeisureSuggestionSource.Backlog => count,
        _ => 1
    };

    // --- weights -------------------------------------------------------------------------------------
    // Named rather than inlined, because these are the argument. Changing one changes what the app
    // recommends, which is a product decision and should read like one in the diff.

    private const double EnergyExactMatch = 2;

    /// <summary>Per step the activity is MORE demanding than the user said they are. The expensive mistake.</summary>
    private const double EnergyTooDemandingPerStep = -2.5;

    /// <summary>Per step the activity is LESS demanding. Merely uninspiring, so a much softer penalty.</summary>
    private const double EnergyTooEasyPerStep = -0.75;

    /// <summary>Full marks for filling the available time, nothing for a rounding error against it.</summary>
    private const double DurationFitMax = 2;

    /// <summary>Sources that state no duration sit at the middle of that range rather than losing to it.</summary>
    private const double DurationFitNeutral = 1;

    private const double EffortVarietyBonus = 1;
    private const double StalenessNeverSuggested = 3;
    private const double StalenessFloor = -4;
    private const double StalenessCeiling = 3;

    /// <summary>Step 1 scores 2.5, step 5 scores 0.5: the smallest untried step is the most actionable one.</summary>
    private const double ComfortStepWeight = 0.5;

    private const double JitterRange = 3;

    private static double SourceWeight(LeisureSuggestionSource source) => source switch
    {
        LeisureSuggestionSource.Backlog => 0,
        LeisureSuggestionSource.Project => -0.5,
        LeisureSuggestionSource.BucketList => -2,
        _ => 0
    };

    private static double ReadinessWeight(ReadinessStatus status) => status switch
    {
        ReadinessStatus.ReadyToStart => 2,
        ReadinessStatus.Planning => -1,
        // Never scored — IsEligible drops it — but the map is total so a new status cannot fall through.
        _ => 0
    };

    private static int EnergyRank(EnergyLevel level) => level switch
    {
        EnergyLevel.Low => 0,
        EnergyLevel.Medium => 1,
        _ => 2
    };

    // --- proxy energy --------------------------------------------------------------------------------

    /// <summary>A project has no energy field; its difficulty is the closest thing the schema records.</summary>
    public static EnergyLevel DifficultyAsEnergy(DifficultyLevel level) => level switch
    {
        DifficultyLevel.Beginner => EnergyLevel.Low,
        DifficultyLevel.Expert => EnergyLevel.High,
        _ => EnergyLevel.Medium
    };

    /// <summary>The same 1/3/5 ramp the comfort-zone colours walk — a step-5 experience is not a low-energy evening.</summary>
    public static EnergyLevel ComfortStepAsEnergy(int step) => step switch
    {
        <= 1 => EnergyLevel.Low,
        <= 3 => EnergyLevel.Medium,
        _ => EnergyLevel.High
    };

    // --- seeded jitter -------------------------------------------------------------------------------

    /// <summary>
    /// FNV-1a, 32-bit. Small, stable, and good enough to decorrelate adjacent keys. Deliberately the same
    /// arithmetic the client's implementation performed (<c>Math.imul</c> over UTF-16 code units), so both
    /// ends agree on a draw.
    /// </summary>
    private static uint HashKey(string key)
    {
        var hash = 0x811c9dc5u;
        foreach (var c in key)
        {
            hash ^= c;
            hash = unchecked(hash * 0x01000193u);
        }

        return hash;
    }

    /// <summary>mulberry32, one draw. Deterministic in (key, seed) and independent of candidate order.</summary>
    private static double JitterFor(string key, uint seed)
    {
        var state = unchecked((HashKey(key) ^ seed) + 0x6d2b79f5u);
        var t = unchecked((state ^ (state >> 15)) * (1u | state));
        t = unchecked((t + (t ^ (t >> 7)) * (61u | t)) ^ t);
        return (t ^ (t >> 14)) / 4294967296d;
    }

    // --- the rule ------------------------------------------------------------------------------------

    /// <summary>
    /// A candidate that fails one of these is not shown at all, however well it would score: an activity
    /// that does not fit the time available is not a suggestion, it is a taunt.
    /// </summary>
    public static bool IsEligible(LeisureCandidate candidate, LeisureDrawConstraints constraints)
    {
        if (candidate.MinParticipants is { } minParticipants && minParticipants > constraints.People)
            return false;

        return candidate.Source switch
        {
            LeisureSuggestionSource.Backlog =>
                candidate.StatedDurationMinutes is null || candidate.StatedDurationMinutes <= constraints.Minutes,
            LeisureSuggestionSource.Project =>
                candidate.ReadinessStatus != ReadinessStatus.NeedsShopping && constraints.Minutes >= ProjectMinMinutes,
            LeisureSuggestionSource.BucketList =>
                constraints.Minutes >= BucketListMinMinutes
                && (!candidate.RequiresTravel || constraints.Minutes >= BucketListTravelMinMinutes),
            _ => false
        };
    }

    /// <summary>
    /// How long to book for when the user commits. Never longer than they said they had, never longer than
    /// the thing is worth, and never so short it is unplannable. The response carries the facts this reads
    /// rather than the answer, because the client decides the slot at commit time.
    /// </summary>
    public static int SlotMinutesFor(LeisureCandidate candidate, LeisureDrawConstraints constraints)
    {
        var useful = candidate.MaxUsefulMinutes ?? constraints.Minutes;
        return Math.Max(Math.Min(useful, constraints.Minutes), MinSlotMinutes);
    }

    private static double EnergyFit(LeisureCandidate candidate, LeisureDrawConstraints constraints)
    {
        var delta = EnergyRank(candidate.EnergyLevel) - EnergyRank(constraints.Energy);
        if (delta == 0)
            return EnergyExactMatch;

        return delta > 0 ? delta * EnergyTooDemandingPerStep : -delta * EnergyTooEasyPerStep;
    }

    private static double DurationFit(LeisureCandidate candidate, LeisureDrawConstraints constraints)
    {
        if (candidate.StatedDurationMinutes is not { } stated || constraints.Minutes <= 0)
            return DurationFitNeutral;

        return DurationFitMax * Math.Min((double)stated / constraints.Minutes, 1);
    }

    private static double Staleness(LeisureCandidate candidate, LeisureRankingContext context)
    {
        if (!context.LastSuggestedAt.TryGetValue(candidate.Key, out var at))
            return StalenessNeverSuggested;

        var days = Math.Max(0, (context.Now - at).TotalDays);
        // One point per day back from the floor: shown today is buried, a week ago is as good as new.
        return Math.Min(Math.Max(StalenessFloor + days, StalenessFloor), StalenessCeiling);
    }

    public static double Score(LeisureCandidate candidate, LeisureRankingContext context)
    {
        var score = EnergyFit(candidate, context.Constraints);
        score += DurationFit(candidate, context.Constraints);
        score += Staleness(candidate, context);
        score += SourceWeight(candidate.Source);

        if (candidate.EffortType is { } effort && context.LastCommittedEffort is { } lastEffort && effort != lastEffort)
            score += EffortVarietyBonus;

        if (candidate.ReadinessStatus is { } readiness)
            score += ReadinessWeight(readiness);

        if (candidate.ComfortZoneStep is { } step)
            score += (6 - step) * ComfortStepWeight;

        return score + JitterFor(candidate.Key, context.Seed) * JitterRange;
    }

    /// <summary>
    /// The draw: filter by the hard constraints, rank by the soft ones, then take <paramref name="count"/>
    /// under the source caps — relaxing the caps rather than returning two cards when the eligible pool is
    /// thin, because the caps exist to keep the mix interesting and never to cost the user a suggestion.
    /// </summary>
    /// <returns>The chosen candidates, best first, and how many survived the hard constraints.</returns>
    public static (List<LeisureCandidate> Chosen, int EligibleCount) Draw(
        IEnumerable<LeisureCandidate> candidates, LeisureRankingContext context, int count)
    {
        var ranked = candidates
            .Where(candidate => IsEligible(candidate, context.Constraints))
            .Select(candidate => (Candidate: candidate, Score: Score(candidate, context)))
            // The key tiebreak is what makes the seed contract hold: scores can tie exactly (two backlog
            // rows with the same duration and energy, both never suggested), and without it the order
            // would fall back to whatever order the rows arrived in — which no seed controls.
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Candidate.Key, StringComparer.Ordinal)
            .ToList();

        var chosen = new List<LeisureCandidate>(count);
        var chosenKeys = new HashSet<string>(StringComparer.Ordinal);
        var usedPerSource = new Dictionary<LeisureSuggestionSource, int>();

        foreach (var (candidate, _) in ranked)
        {
            if (chosen.Count == count)
                break;

            usedPerSource.TryGetValue(candidate.Source, out var used);
            if (used >= SourceCap(candidate.Source, count))
                continue;

            usedPerSource[candidate.Source] = used + 1;
            chosen.Add(candidate);
            chosenKeys.Add(candidate.Key);
        }

        foreach (var (candidate, _) in ranked)
        {
            if (chosen.Count == count)
                break;
            if (chosenKeys.Add(candidate.Key))
                chosen.Add(candidate);
        }

        return (chosen, ranked.Count);
    }
}
