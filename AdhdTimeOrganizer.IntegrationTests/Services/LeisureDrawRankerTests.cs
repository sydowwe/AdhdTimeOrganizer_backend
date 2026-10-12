using AdhdTimeOrganizer.ActivityProfiles.domain.model;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.ActivityProfiles.domain.service;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Services;

/// <summary>
/// The leisure draw's rule, tested without a database — the whole reason <see cref="LeisureDrawRanker"/> is
/// pure. These are the claims the endpoint's contract rests on: what is excluded rather than penalised, that a
/// seed reproduces a draw, and that the source caps shape a normal draw without ever costing the user a card.
/// </summary>
public class LeisureDrawRankerTests
{
    private static readonly DateTime Now = new(2026, 8, 17, 18, 0, 0, DateTimeKind.Utc);

    private static LeisureCandidate Backlog(long id, int? duration = 30, EnergyLevel energy = EnergyLevel.Low,
        int? minParticipants = 1, EffortType? effort = null) =>
        new(LeisureSuggestionSource.Backlog, id, duration, duration, energy, false, effort, minParticipants,
            null, null, false);

    private static LeisureCandidate Project(long id, ReadinessStatus readiness = ReadinessStatus.ReadyToStart,
        DifficultyLevel difficulty = DifficultyLevel.Beginner) =>
        new(LeisureSuggestionSource.Project, id, null, 600,
            LeisureDrawRanker.DifficultyAsEnergy(difficulty), true, null, null, readiness, null, false);

    private static LeisureCandidate BucketList(long id, int step = 2, bool requiresTravel = false) =>
        new(LeisureSuggestionSource.BucketList, id, null, null,
            LeisureDrawRanker.ComfortStepAsEnergy(step), true, null, null, null, step, requiresTravel);

    private static LeisureRankingContext Context(int minutes = 240, EnergyLevel energy = EnergyLevel.Low,
        int people = 1, uint seed = 1837465239, IReadOnlyDictionary<string, DateTime>? history = null,
        EffortType? lastCommittedEffort = null) =>
        new(new LeisureDrawConstraints(minutes, energy, people), history ?? new Dictionary<string, DateTime>(),
            lastCommittedEffort, Now, seed);

    // ─── hard constraints ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ABacklogActivityLongerThanTheTimeAvailable_IsNotOffered()
    {
        var constraints = new LeisureDrawConstraints(30, EnergyLevel.Low, 1);

        LeisureDrawRanker.IsEligible(Backlog(1, duration: 45), constraints).Should().BeFalse();
        LeisureDrawRanker.IsEligible(Backlog(2, duration: 30), constraints).Should().BeTrue();
    }

    [Fact]
    public void ABacklogActivityWithNoDurationRecorded_StaysEligible()
    {
        // 0 minutes in the column means "never filled in", not "takes no time". Dropping these would hide a new
        // user's whole backlog behind a constraint they never set.
        LeisureDrawRanker.IsEligible(Backlog(1, duration: null), new LeisureDrawConstraints(15, EnergyLevel.Low, 1))
            .Should().BeTrue();
    }

    [Fact]
    public void AnActivityNeedingMorePeopleThanAreAround_IsNotOffered()
    {
        var alone = new LeisureDrawConstraints(240, EnergyLevel.Low, 1);

        LeisureDrawRanker.IsEligible(Backlog(1, minParticipants: 2), alone).Should().BeFalse();
        LeisureDrawRanker.IsEligible(Backlog(2, minParticipants: 1), alone).Should().BeTrue();
    }

    [Fact]
    public void AProjectThatNeedsShopping_IsNeverOffered()
    {
        var plenty = new LeisureDrawConstraints(600, EnergyLevel.High, 1);

        LeisureDrawRanker.IsEligible(Project(1, ReadinessStatus.NeedsShopping), plenty).Should().BeFalse();
        LeisureDrawRanker.IsEligible(Project(2, ReadinessStatus.Planning), plenty).Should().BeTrue();
    }

    [Theory]
    // A project session below the floor is not worth the setup...
    [InlineData(59, false)]
    [InlineData(60, true)]
    public void AProjectNeedsARealBlockOfTime(int minutes, bool expected) =>
        LeisureDrawRanker.IsEligible(Project(1), new LeisureDrawConstraints(minutes, EnergyLevel.Low, 1))
            .Should().Be(expected);

    [Theory]
    // ...and a bucket-list experience is never a filler. Travel raises the floor again.
    [InlineData(119, false, false)]
    [InlineData(120, false, true)]
    [InlineData(120, true, false)]
    [InlineData(240, true, true)]
    public void ABucketListExperienceNeedsMostOfAnEvening_AndMoreIfItTravels(int minutes, bool travels, bool expected) =>
        LeisureDrawRanker.IsEligible(BucketList(1, requiresTravel: travels),
            new LeisureDrawConstraints(minutes, EnergyLevel.Low, 1)).Should().Be(expected);

    [Fact]
    public void AProjectsWholeEstimate_IsNotATimeConstraint()
    {
        // The estimate covers the whole build, not one sitting: two free hours is a perfectly good start on a
        // ten-hour project, and excluding it would be wrong.
        var candidate = Project(1) with { MaxUsefulMinutes = 600 };

        LeisureDrawRanker.IsEligible(candidate, new LeisureDrawConstraints(120, EnergyLevel.Low, 1))
            .Should().BeTrue();
    }

    // ─── the seed contract ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheSameSeedOverTheSamePool_DrawsTheSameCardsInTheSameOrder()
    {
        var pool = Enumerable.Range(1, 12).Select(i => Backlog(i)).ToList();

        var first = LeisureDrawRanker.Draw(pool, Context(seed: 42), 3).Chosen;
        var second = LeisureDrawRanker.Draw(pool, Context(seed: 42), 3).Chosen;

        second.Select(c => c.Key).Should().Equal(first.Select(c => c.Key));
    }

    [Fact]
    public void TheDrawDoesNotDependOnTheOrderTheRowsArrivedIn()
    {
        // The jitter hashes the candidate key rather than walking a sequential RNG, so three fetches resolving in
        // a different order must not reshuffle the cards.
        var pool = Enumerable.Range(1, 12).Select(i => Backlog(i)).ToList();
        var reversed = Enumerable.Reverse(pool).ToList();

        var forwards = LeisureDrawRanker.Draw(pool, Context(seed: 7), 3).Chosen;
        var backwards = LeisureDrawRanker.Draw(reversed, Context(seed: 7), 3).Chosen;

        backwards.Select(c => c.Key).Should().Equal(forwards.Select(c => c.Key));
    }

    [Fact]
    public void AnotherSeed_DrawsSomethingElse()
    {
        // "Something else" has to actually produce something else, or the reroll button is decoration. Swept
        // rather than spot-checked so the assertion is deterministic.
        var pool = Enumerable.Range(1, 12).Select(i => Backlog(i)).ToList();
        var baseline = LeisureDrawRanker.Draw(pool, Context(seed: 1), 3).Chosen.Select(c => c.Key).ToList();

        var distinctDraws = Enumerable.Range(2, 30)
            .Select(seed => LeisureDrawRanker.Draw(pool, Context(seed: (uint)seed), 3).Chosen.Select(c => c.Key).ToList())
            .Count(draw => !draw.SequenceEqual(baseline));

        distinctDraws.Should().BeGreaterThan(0);
    }

    // ─── the soft signals ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SomethingShownToday_RanksBelowTheSameThingNeverShown()
    {
        var shown = Backlog(1);
        var fresh = Backlog(2);
        var history = new Dictionary<string, DateTime> { [shown.Key] = Now };

        var context = Context(history: history);

        LeisureDrawRanker.Score(shown, context).Should().BeLessThan(LeisureDrawRanker.Score(fresh, context) - 3,
            "shown-today is buried by a margin the jitter cannot bridge");
    }

    [Fact]
    public void SomethingShownAWeekAgo_IsAsGoodAsNew()
    {
        var stale = Backlog(1);
        var context = Context(history: new Dictionary<string, DateTime> { [stale.Key] = Now.AddDays(-7) });
        var never = Context();

        LeisureDrawRanker.Score(stale, context).Should().Be(LeisureDrawRanker.Score(stale, never));
    }

    [Fact]
    public void TooDemandingIsPenalisedHarderThanTooEasy()
    {
        var tired = Context(energy: EnergyLevel.Medium);
        var demanding = Backlog(1, energy: EnergyLevel.High);
        var easy = Backlog(1, energy: EnergyLevel.Low);

        // Same key on both, so the jitter is identical and the energy term is the only difference.
        LeisureDrawRanker.Score(demanding, tired).Should().BeLessThan(LeisureDrawRanker.Score(easy, tired));
    }

    [Fact]
    public void ADifferentEffortTypeFromTheLastThingCommittedTo_IsPreferred()
    {
        var context = Context(lastCommittedEffort: EffortType.Physical);
        var variety = Backlog(1, effort: EffortType.Mental);
        var sameAgain = Backlog(1, effort: EffortType.Physical);

        LeisureDrawRanker.Score(variety, context).Should().BeGreaterThan(LeisureDrawRanker.Score(sameAgain, context));
    }

    // ─── weather and party size (F2-04: the two signals only the client's ranker had) ─────────────────
    //
    // Ported from the client's leisureScoring.test.ts when the picker moved onto this rule, with the client's
    // weights: a 1.5 bonus for a matching real dependency, a flat -2 when more people are around than the
    // activity takes. Same candidate key on both sides of each comparison, so the jitter cancels and the
    // difference is the signal alone.

    private static readonly IReadOnlySet<string> FineDay =
        new HashSet<string> { WeatherDependencyCodes.None, WeatherDependencyCodes.Dry, WeatherDependencyCodes.Sunny };

    private static LeisureCandidate WeatherBacklog(long id, string? weatherCode) =>
        new(LeisureSuggestionSource.Backlog, id, 30, 30, EnergyLevel.Low, false, null, 1, null, null, false,
            WeatherCode: weatherCode);

    private static LeisureRankingContext WeatherContext(IReadOnlySet<string>? codes) =>
        new(new LeisureDrawConstraints(240, EnergyLevel.Low, 1), new Dictionary<string, DateTime>(), null, Now, 1837465239,
            codes);

    [Fact]
    public void ARealDependencyTodayDoesNotFit_IsNotOffered_OnlyWhenTheSignalIsKnown()
    {
        var constraints = new LeisureDrawConstraints(240, EnergyLevel.Low, 1);
        var wantsSnow = WeatherBacklog(1, WeatherDependencyCodes.Snow);

        LeisureDrawRanker.IsEligible(wantsSnow, constraints, FineDay).Should().BeFalse("snow in July is a taunt");
        LeisureDrawRanker.IsEligible(wantsSnow, constraints, weatherMatchCodes: null)
            .Should().BeTrue("no signal is no opinion, never a mismatch");
        LeisureDrawRanker.IsEligible(WeatherBacklog(2, WeatherDependencyCodes.Sunny), constraints, FineDay).Should().BeTrue();
    }

    [Fact]
    public void AnEntryWithNoRealDependency_IsNeverExcludedOnWeather()
    {
        // The endpoint passes null for a "none" row and for a label that infers nothing.
        var snowDay = new HashSet<string> { WeatherDependencyCodes.None, WeatherDependencyCodes.Snow };

        LeisureDrawRanker.IsEligible(WeatherBacklog(1, weatherCode: null), new LeisureDrawConstraints(240, EnergyLevel.Low, 1), snowDay)
            .Should().BeTrue();
    }

    [Fact]
    public void AMatchingRealDependency_RanksUpByTheBonus()
    {
        var sunny = WeatherBacklog(1, WeatherDependencyCodes.Sunny);

        var withSignal = LeisureDrawRanker.Score(sunny, WeatherContext(FineDay));
        var withoutSignal = LeisureDrawRanker.Score(sunny, WeatherContext(null));

        (withSignal - withoutSignal).Should().BeApproximately(1.5, 1e-9);
    }

    [Fact]
    public void AnEntryWithNoRealDependency_GetsNoWeatherBonus()
    {
        var indoor = WeatherBacklog(1, weatherCode: null);

        LeisureDrawRanker.Score(indoor, WeatherContext(FineDay))
            .Should().Be(LeisureDrawRanker.Score(indoor, WeatherContext(null)),
                "a none row is in every fit set, so boosting it would boost every indoor activity");
    }

    [Fact]
    public void MorePeopleThanTheActivityTakes_IsDemotedNotDropped()
    {
        var twoPlayer = new LeisureCandidate(LeisureSuggestionSource.Backlog, 1, 30, 30, EnergyLevel.Low, false, null, 1,
            null, null, false, MaxParticipants: 2);
        var constraints = new LeisureDrawConstraints(240, EnergyLevel.Low, 4);

        LeisureDrawRanker.IsEligible(twoPlayer, constraints).Should().BeTrue("a two-player game is still something to do");
        (LeisureDrawRanker.Score(twoPlayer, Context(people: 4)) - LeisureDrawRanker.Score(twoPlayer, Context(people: 2)))
            .Should().BeApproximately(-2, 1e-9, "flat, not per extra person");
    }

    // ─── what actually happened (F3-06) ──────────────────────────────────────────────────────────────

    private static LeisureRankingContext DoneContext(IReadOnlyDictionary<long, DateTime>? lastDone = null,
        IReadOnlyDictionary<long, int>? unkept = null, IReadOnlyDictionary<string, DateTime>? history = null) =>
        new(new LeisureDrawConstraints(240, EnergyLevel.Low, 1), history ?? new Dictionary<string, DateTime>(), null, Now,
            1837465239, null, lastDone, unkept);

    [Fact]
    public void SomethingDoneYesterday_RanksBelowItself_NeverDone_EvenIfNeverShown()
    {
        var walk = Backlog(1);

        LeisureDrawRanker.Score(walk, DoneContext(lastDone: new Dictionary<long, DateTime> { [1] = Now.AddDays(-1) }))
            .Should().BeLessThan(LeisureDrawRanker.Score(walk, DoneContext()),
                "done yesterday is buried tonight exactly as if it had been shown yesterday");
    }

    [Fact]
    public void StalenessReadsTheLaterOfShownAndDone()
    {
        var walk = Backlog(1);
        var shownLongAgo = new Dictionary<string, DateTime> { [walk.Key] = Now.AddDays(-20) };
        var doneYesterday = new Dictionary<long, DateTime> { [1] = Now.AddDays(-1) };
        var shownYesterday = new Dictionary<string, DateTime> { [walk.Key] = Now.AddDays(-1) };

        LeisureDrawRanker.Score(walk, DoneContext(doneYesterday, history: shownLongAgo))
            .Should().Be(LeisureDrawRanker.Score(walk, DoneContext(history: shownYesterday)));
    }

    [Fact]
    public void DoneAWeekAgo_IsAsGoodAsNew()
    {
        var walk = Backlog(1);

        LeisureDrawRanker.Score(walk, DoneContext(lastDone: new Dictionary<long, DateTime> { [1] = Now.AddDays(-8) }))
            .Should().Be(LeisureDrawRanker.Score(walk, DoneContext()));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, -1.5)]
    [InlineData(5, -1.5)]
    public void UnkeptCommitments_DemoteSoftly_FromTheSecond(int unkept, double expectedDelta)
    {
        var boardGames = Backlog(1);

        (LeisureDrawRanker.Score(boardGames, DoneContext(unkept: new Dictionary<long, int> { [1] = unkept }))
         - LeisureDrawRanker.Score(boardGames, DoneContext()))
            .Should().BeApproximately(expectedDelta, 1e-9, "one missed evening is just an evening; it never excludes");
    }

    [Fact]
    public void UnkeptCommitments_NeverExclude()
    {
        LeisureDrawRanker.Draw([Backlog(1)], DoneContext(unkept: new Dictionary<long, int> { [1] = 9 }), 3)
            .Chosen.Should().ContainSingle();
    }

    // ─── source caps ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ANormalDraw_TakesAtMostOneProjectAndOneBucketListEntry()
    {
        var pool = new List<LeisureCandidate>
        {
            Project(1), Project(2), Project(3),
            BucketList(4), BucketList(5), BucketList(6),
            Backlog(7), Backlog(8)
        };

        var chosen = LeisureDrawRanker.Draw(pool, Context(), 3).Chosen;

        chosen.Should().HaveCount(3);
        chosen.Count(c => c.Source == LeisureSuggestionSource.Project).Should().BeLessThanOrEqualTo(1);
        chosen.Count(c => c.Source == LeisureSuggestionSource.BucketList).Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void ThinPool_RelaxesTheCapsRatherThanReturningFewerCards()
    {
        // The caps exist to keep the mix interesting, never to cost the user a suggestion.
        var pool = new List<LeisureCandidate> { Project(1), Project(2), Project(3), Project(4) };

        var (chosen, eligible) = LeisureDrawRanker.Draw(pool, Context(), 3);

        eligible.Should().Be(4);
        chosen.Should().HaveCount(3);
        chosen.Select(c => c.Key).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void EligibleCount_CountsWhatSurvivedTheConstraints_NotWhatWasDrawn()
    {
        var pool = new List<LeisureCandidate>
        {
            Backlog(1, duration: 30),
            Backlog(2, duration: 300),   // longer than the time available
            Backlog(3, minParticipants: 4), // needs a group
            Project(4),
            BucketList(5, requiresTravel: true) // needs most of a day
        };

        var (chosen, eligible) = LeisureDrawRanker.Draw(pool, Context(minutes: 120, people: 1), 3);

        eligible.Should().Be(2, "one backlog row and the project survive a two-hour, solo evening");
        chosen.Should().HaveCount(2);
    }

    // ─── slot sizing ─────────────────────────────────────────────────────────────────────────────────

    [Theory]
    // Never longer than the user said they had...
    [InlineData(300, 60, 60)]
    // ...never longer than the thing is worth...
    [InlineData(45, 120, 45)]
    // ...and never so short it is unplannable.
    [InlineData(2, 120, LeisureDrawRanker.MinSlotMinutes)]
    public void ASlotIsNeverLongerThanEitherBound_NorShorterThanIsPlannable(int useful, int available, int expected)
    {
        var candidate = Backlog(1) with { MaxUsefulMinutes = useful };

        LeisureDrawRanker.SlotMinutesFor(candidate, new LeisureDrawConstraints(available, EnergyLevel.Low, 1))
            .Should().Be(expected);
    }

    [Fact]
    public void ASourceThatRecordsNoDuration_BooksTheTimeTheUserSaidTheyHad()
    {
        LeisureDrawRanker.SlotMinutesFor(BucketList(1), new LeisureDrawConstraints(180, EnergyLevel.Low, 1))
            .Should().Be(180);
    }
}
