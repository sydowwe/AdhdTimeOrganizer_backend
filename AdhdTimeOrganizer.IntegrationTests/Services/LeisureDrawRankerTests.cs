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
