using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.service;

namespace AdhdTimeOrganizer.Planning.domain.model.entity.suggestion;

/// <summary>
/// FO11. "×, not any more" on a pattern suggestion: the user saying this activity no longer happens on this
/// weekday (or this day of the month), whatever the statistics say.
/// </summary>
/// <remarks>
/// <para>
/// Keyed like the pattern it hides — (activity, pattern type, pattern value) — so one dismissal hides the
/// planner-pattern and the history-pattern row alike, and never the user-set recurrences, which are the user's
/// own decision and have their own delete.
/// </para>
/// <para>
/// It lapses after one window length (<see cref="SuggestionPatternWindow.StillHolds"/>); see that type for why
/// that is the right moment for a pattern to be allowed back.
/// </para>
/// </remarks>
public class PlannerSuggestionDismissal : BaseEntityWithActivity
{
    /// <summary><see cref="RecurrenceType.DayOfWeek"/> or <see cref="RecurrenceType.DayOfMonth"/> — the two kinds a pattern can be.</summary>
    public RecurrenceType PatternType { get; set; }

    /// <summary>ISO weekday (1 = Monday … 7 = Sunday) or day of the month (1–31), as in the pattern views.</summary>
    public int PatternValue { get; set; }

    /// <summary>UTC date of the most recent dismissal; a repeat dismissal moves it forward.</summary>
    public DateOnly DismissedOn { get; set; }
}
