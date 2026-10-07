using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.service;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Services;

/// <summary>
/// FO4's placement rule without a database: first fit in priority order, lengths kept, optional blocks giving way
/// first, and the window's end. The routes that feed it are pinned over HTTP in <c>Endpoints.DayReplanTests</c>.
/// </summary>
public class DayPackingTests
{
    private const int EndOfDay = DayPacking.LastEndMinute;

    private static int M(int hour, int minute = 0) => hour * 60 + minute;

    private static DayPacking.Span S(int fromHour, int fromMinute, int toHour, int toMinute) =>
        new(M(fromHour, fromMinute), M(toHour, toMinute));

    private static DayPacking.Item Slipped(long id, int minutes, int from, bool optional = false, int until = EndOfDay) =>
        new(id, minutes, from, until, optional);

    [Fact(DisplayName = "FO4: a fixed block splits the free time, and a shorter later block back-fills the gap a longer one cannot use")]
    public void FixedBlock_SplitsTheTime_AndAShortBlockBackFills()
    {
        var result = DayPacking.Pack([S(12, 0, 13, 0)], [Slipped(1, 60, M(11, 30)), Slipped(2, 20, M(11, 30))]);

        result.Placed[1].Should().Be(S(13, 0, 14, 0), "11:30–12:00 is too short for an hour");
        result.Placed[2].Should().Be(S(11, 30, 11, 50));
        result.WontFit.Should().BeEmpty();
    }

    [Fact(DisplayName = "FO4: a block longer than every free stretch does not fit, and the rest still do")]
    public void LongerThanEveryGap_DoesNotFit()
    {
        var result = DayPacking.Pack(
            [S(12, 0, 13, 0)],
            [Slipped(1, 90, M(11, 30), until: M(14, 0)), Slipped(2, 30, M(11, 30), until: M(14, 0))]);

        result.WontFit.Should().Equal(1);
        result.Placed[2].Should().Be(S(11, 30, 12, 0));
    }

    [Fact(DisplayName = "FO4: when a required block would miss out, optional ones give way first")]
    public void OptionalGivesWay_WhenARequiredBlockWouldMissOut()
    {
        // 22:00–23:59 holds two hours less a minute: in order the optional block takes the first hour and the
        // second required one is left over.
        var result = DayPacking.Pack([],
        [
            Slipped(1, 60, M(22), optional: true),
            Slipped(2, 60, M(22)),
            Slipped(3, 45, M(22))
        ]);

        result.Placed[2].Should().Be(S(22, 0, 23, 0));
        result.Placed[3].Should().Be(S(23, 0, 23, 45));
        result.WontFit.Should().Equal(1);
    }

    [Fact(DisplayName = "FO4: when only optional blocks miss out, the in-order packing stands")]
    public void OnlyOptionalMissingOut_KeepsTheOrder()
    {
        var result = DayPacking.Pack([],
        [
            Slipped(1, 30, M(22), optional: true),
            Slipped(2, 60, M(22)),
            Slipped(3, 60, M(22), optional: true)
        ]);

        result.Placed[1].Should().Be(S(22, 0, 22, 30), "nothing required lost out, so the optional block keeps its turn");
        result.Placed[2].Should().Be(S(22, 30, 23, 30));
        result.WontFit.Should().Equal(3);
    }

    [Fact(DisplayName = "FO4: with no time left everything comes back, optional first and then in order")]
    public void NoTimeLeft_EverythingWontFit_OptionalFirst()
    {
        var result = DayPacking.Pack([],
        [
            Slipped(1, 60, EndOfDay),
            Slipped(2, 30, EndOfDay, optional: true),
            Slipped(3, 45, EndOfDay)
        ]);

        result.Placed.Should().BeEmpty();
        result.WontFit.Should().Equal(2, 1, 3);
    }

    [Fact(DisplayName = "FO4: a block never starts before its earliest start, and is pushed later when its own slot is taken")]
    public void EarliestStart_IsKept_AndADisplacedBlockMovesLater()
    {
        var result = DayPacking.Pack([],
        [
            Slipped(1, 60, M(11, 30)),
            new DayPacking.Item(2, 60, EarliestStart: M(12), LatestEnd: EndOfDay, IsOptional: false)
        ]);

        result.Placed[1].Should().Be(S(11, 30, 12, 30));
        result.Placed[2].Should().Be(S(12, 30, 13, 30));
    }

    [Theory(DisplayName = "FO4: the window ends at bed time — 00:00, or a bed time after midnight, means the end of the day")]
    [InlineData(0, 0, EndOfDay)]
    [InlineData(23, 0, 23 * 60)]
    [InlineData(1, 30, EndOfDay)]
    [InlineData(7, 0, EndOfDay)]
    [InlineData(22, 15, 22 * 60 + 15)]
    public void WindowEnd_FollowsBedTime(int bedHour, int bedMinute, int expected) =>
        DayPacking.WindowEnd(new TimeOnly(7, 0), new TimeOnly(bedHour, bedMinute)).Should().Be(expected);

    [Fact(DisplayName = "FO4: the next free slot is the earliest stretch long enough, or none")]
    public void FirstFreeSlot_TakesTheEarliestLongEnoughStretch()
    {
        DayPacking.Span[] occupied = [S(9, 0, 10, 0), S(10, 30, 12, 0)];

        DayPacking.FirstFreeSlot(occupied, 30, M(9), EndOfDay).Should().Be(S(10, 0, 10, 30));
        DayPacking.FirstFreeSlot(occupied, 45, M(9), EndOfDay).Should().Be(S(12, 0, 12, 45));
        DayPacking.FirstFreeSlot(occupied, 45, M(9), M(12, 30)).Should().BeNull("12:00–12:30 is all that is left before the end");
        DayPacking.FirstFreeSlot(occupied, 0, M(9), EndOfDay).Should().BeNull();
    }

    [Fact(DisplayName = "FO4: a block wrapping midnight holds the rest of the day; an empty one holds nothing")]
    public void Occupancy_OfOddSpans()
    {
        DayPacking.Occupancy(new TimeOnly(23, 0), new TimeOnly(1, 0)).Should().Be(new DayPacking.Span(M(23), DayPacking.MinutesPerDay));
        DayPacking.Occupancy(new TimeOnly(10, 0), new TimeOnly(10, 0)).Length.Should().Be(0);
        DayPacking.CeilingMinuteOf(new TimeOnly(11, 30, 1)).Should().Be(M(11, 31));
        DayPacking.CeilingMinuteOf(new TimeOnly(11, 30)).Should().Be(M(11, 30));
    }
}

/// <summary>FO4's one definition of a missed block, and the late / upcoming states beside it.</summary>
public class PlannerTaskMissedRuleTests
{
    private static readonly DateOnly Today = new(2030, 3, 12);

    private static PlannerTaskSlipState? Classify(string start, string end, string now,
        PlannerTaskStatus status = PlannerTaskStatus.NotStarted, bool background = false, DateOnly? nowDate = null) =>
        PlannerTaskMissedRule.Classify(status, background, Today, TimeOnly.Parse(start), TimeOnly.Parse(end),
            (nowDate ?? Today).ToDateTime(TimeOnly.Parse(now)));

    [Fact(DisplayName = "FO4: not started and its end has passed on the user's clock is missed")]
    public void EndPassed_IsMissed()
    {
        Classify("10:00", "11:00", "11:30").Should().Be(PlannerTaskSlipState.Missed);
        Classify("10:00", "11:00", "11:00").Should().Be(PlannerTaskSlipState.Missed, "the end is the last moment it could have been done");
        PlannerTaskMissedRule.IsMissed(PlannerTaskStatus.NotStarted, false, Today, new TimeOnly(10, 0), new TimeOnly(11, 0),
            Today.ToDateTime(new TimeOnly(11, 30))).Should().BeTrue();
    }

    [Fact(DisplayName = "FO4: a slot under way without the user is late, and one at its very start minute is still upcoming")]
    public void Underway_IsLate_AtItsStart_IsUpcoming()
    {
        Classify("11:00", "12:00", "11:30").Should().Be(PlannerTaskSlipState.Late);
        Classify("11:00", "12:00", "11:00").Should().Be(PlannerTaskSlipState.Upcoming);
        Classify("14:00", "15:00", "11:30").Should().Be(PlannerTaskSlipState.Upcoming);
    }

    [Theory(DisplayName = "FO4: a block begun, closed or in the background is never missed")]
    [InlineData(PlannerTaskStatus.InProgress, false)]
    [InlineData(PlannerTaskStatus.OnHold, false)]
    [InlineData(PlannerTaskStatus.Completed, false)]
    [InlineData(PlannerTaskStatus.Cancelled, false)]
    [InlineData(PlannerTaskStatus.NotStarted, true)]
    public void BegunClosedOrBackground_IsOutOfScope(PlannerTaskStatus status, bool background) =>
        Classify("08:00", "09:00", "11:30", status, background).Should().BeNull();

    [Fact(DisplayName = "FO4: a legacy block wrapping midnight is missed only once its end on the next day has passed")]
    public void WrappingBlock_EndsOnTheNextDay()
    {
        Classify("23:00", "01:00", "23:30").Should().Be(PlannerTaskSlipState.Late);
        Classify("23:00", "01:00", "00:30", nowDate: Today.AddDays(1)).Should().Be(PlannerTaskSlipState.Late);
        Classify("23:00", "01:00", "01:00", nowDate: Today.AddDays(1)).Should().Be(PlannerTaskSlipState.Missed);
    }
}
