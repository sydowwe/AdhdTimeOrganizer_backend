using FluentAssertions;
using Sydowwe.Framework.infrastructure.persistence.seeder;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Seeding;

/// <summary>
/// Unit tests — no database. The matcher is the whole of the per-user seeders' decision-making
/// (<see cref="BasePerUserDefaultSeeder{TEntity}"/> only wraps it in a query and a SaveChanges), and
/// both bugs it was written to kill are shaped as lists rather than as rows, so they reproduce here
/// without a container.
/// </summary>
public class PerUserDefaultMatcherTests
{
    private sealed class Row(string key, string payload = "")
    {
        public string Key { get; set; } = key;
        public string Payload { get; set; } = payload;
    }

    private static bool Collides(Row a, Row b) => a.Key == b.Key;

    private static List<Row> Defaults() => [new("a"), new("b"), new("c")];

    // ---- Missing ---------------------------------------------------------------------------

    [Fact]
    public void Missing_ReturnsEveryDefault_WhenUserHasNothing()
    {
        var missing = PerUserDefaultMatcher.Missing(Defaults(), [], Collides);

        missing.Select(r => r.Key).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Missing_ReturnsOnlyTheGap_WhenUserHoldsSomeDefaults()
    {
        // The original bug: a user holding "a" and "c" looked unseeded by row count, so the whole
        // set was re-inserted and the unique index rejected the duplicates.
        List<Row> existing = [new("a"), new("c")];

        var missing = PerUserDefaultMatcher.Missing(Defaults(), existing, Collides);

        missing.Select(r => r.Key).Should().Equal("b");
    }

    [Fact]
    public void Missing_ReturnsNothing_WhenUserHoldsAllDefaults()
    {
        List<Row> existing = [new("c"), new("a"), new("b"), new("custom")];

        PerUserDefaultMatcher.Missing(Defaults(), existing, Collides).Should().BeEmpty();
    }

    // ---- MatchForReset ---------------------------------------------------------------------

    [Fact]
    public void MatchForReset_ReturnsNull_WhenUserHasFewerRowsThanDefaults()
    {
        List<Row> existing = [new("a"), new("b")];

        PerUserDefaultMatcher.MatchForReset(Defaults(), existing, Collides).Should().BeNull();
    }

    [Fact]
    public void MatchForReset_MatchesOnTheKey_NotOnPosition()
    {
        // The rename-collision bug: positionally, default "a" would be written onto the row holding
        // "c" while the row holding "a" still exists — a unique violation. Keyed, each default lands
        // on the row that already carries it and no key column is rewritten at all.
        var rowC = new Row("c");
        var rowA = new Row("a");
        var rowB = new Row("b");
        List<Row> existing = [rowC, rowA, rowB];

        var matches = PerUserDefaultMatcher.MatchForReset(Defaults(), existing, Collides);

        matches.Should().NotBeNull();
        matches!.Select(m => m.Target).Should().Equal(rowA, rowB, rowC);
        matches.Should().OnlyContain(m => m.Default.Key == m.Target.Key);
    }

    [Fact]
    public void MatchForReset_GivesUnmatchedDefaultsTheLeftoverRows()
    {
        // "b" was renamed by the user; its row is the one left over once "a" and "c" are claimed,
        // and it provably holds none of the defaults' keys, so writing "b" onto it is safe.
        var rowA = new Row("a");
        var renamed = new Row("mine");
        var rowC = new Row("c");
        List<Row> existing = [rowA, renamed, rowC];

        var matches = PerUserDefaultMatcher.MatchForReset(Defaults(), existing, Collides);

        matches.Should().NotBeNull();
        matches!.Select(m => m.Target).Should().Equal(rowA, renamed, rowC);
    }

    [Fact]
    public void MatchForReset_MatchesDefaultsOutsideTheFirstNRows()
    {
        // Reset used to read only the first defaults.Count rows by id. A default living further down
        // the table was invisible to the match and got rewritten onto an earlier row while its own
        // row survived — the same collision, from the windowing rather than the ordering.
        var mine1 = new Row("mine-1");
        var mine2 = new Row("mine-2");
        var mine3 = new Row("mine-3");
        var rowA = new Row("a");
        var rowB = new Row("b");
        var rowC = new Row("c");
        List<Row> existing = [mine1, mine2, mine3, rowA, rowB, rowC];

        var matches = PerUserDefaultMatcher.MatchForReset(Defaults(), existing, Collides);

        matches.Should().NotBeNull();
        matches!.Select(m => m.Target).Should().Equal(rowA, rowB, rowC);
    }

    [Fact]
    public void MatchForReset_ReturnsNull_WhenOneRowCollidesWithTwoDefaults()
    {
        // Two unique indexes (RoutineTimePeriod): one row can carry default X's text and default Y's
        // length. There is no non-arbitrary way to split it, and guessing means a 23505.
        List<Row> defaults = [new("a"), new("b")];
        List<Row> existing = [new("a"), new("unrelated")];

        // "b" collides with the row already claimed by "a" — the two-unique-index case.
        bool TwoKeys(Row x, Row y) => x.Key == y.Key || (x.Key == "b" && y.Key == "a");

        PerUserDefaultMatcher.MatchForReset(defaults, existing, TwoKeys).Should().BeNull();
    }

    [Fact]
    public void MatchForReset_LeavesTargetsUntouched()
    {
        // The matcher only pairs; copying fields is the seeder's Apply.
        var rowA = new Row("a", "user payload");
        List<Row> existing = [rowA, new("b"), new("c")];

        PerUserDefaultMatcher.MatchForReset(Defaults(), existing, Collides).Should().NotBeNull();

        rowA.Payload.Should().Be("user payload");
    }
}
