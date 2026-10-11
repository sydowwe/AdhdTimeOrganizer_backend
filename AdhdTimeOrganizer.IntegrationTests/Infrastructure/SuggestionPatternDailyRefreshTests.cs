using AdhdTimeOrganizer.infrastructure.persistence.interceptors;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Infrastructure;

/// <summary>
/// FO11. The two pattern views count a window measured from <c>current_date</c> when they refresh, and they used
/// to refresh only after a save touched their tables — so a quiet week froze a pattern the user had already let
/// lapse. <see cref="SuggestionPatternRefreshQueue"/> now reports both as dirty on the first drain of each UTC
/// date. Pure: no database, the clock is in the test's hands.
/// </summary>
public class SuggestionPatternDailyRefreshTests
{
    private sealed class MovableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact(DisplayName = "FO11: the windowed views are dirty once per UTC date, with or without a write")]
    public void WindowedViews_AreDirtyOncePerDay()
    {
        var clock = new MovableClock(new DateTimeOffset(2026, 10, 11, 23, 0, 0, TimeSpan.Zero));
        var queue = new SuggestionPatternRefreshQueue(clock);

        queue.DrainDirty().Should().BeEquivalentTo(
            [SuggestionPatternView.PlannerTask, SuggestionPatternView.ActivityHistory],
            "the first drain of a day refreshes both windowed views — on a fresh boot, straight away");

        clock.Now = clock.Now.AddMinutes(59);
        queue.DrainDirty().Should().BeEmpty("the same date has been marked already, and nothing was written");

        queue.MarkDirty(SuggestionPatternView.TemplateSuggestion);
        queue.DrainDirty().Should().BeEquivalentTo([SuggestionPatternView.TemplateSuggestion],
            "a write still marks only its own view");

        clock.Now = clock.Now.AddMinutes(2); // 00:01 the next day
        queue.DrainDirty().Should().BeEquivalentTo(
            [SuggestionPatternView.PlannerTask, SuggestionPatternView.ActivityHistory],
            "a new date moves the window, so both are stale even though nothing was written");
    }
}
