using AdhdTimeOrganizer.Planning.domain.model.@enum;

namespace AdhdTimeOrganizer.Planning.domain.service;

/// <summary>
/// FO11. How far back a suggestion pattern looks, and so how long a dismissal of one holds.
/// </summary>
/// <remarks>
/// <para>
/// A pattern is what the user does <b>now</b>, not what they did once: "Tuesday: Swimming 18:00" from a course
/// dropped last spring should stop being offered without the user having to say so. Both pattern views
/// (<c>mv_planner_task_pattern.sql</c>, <c>mv_activity_history_pattern.sql</c>) therefore count only sittings
/// inside the window, and drop a pattern whose most recent sitting is older than <see cref="WeeklyLastSeenDays"/>
/// / <see cref="MonthlyLastSeenDays"/> — three missed weeks, or three missed months.
/// </para>
/// <para>
/// ⚠ <b>The SQL repeats these numbers literally</b> — a materialized view takes no parameters. Change one side
/// and change the other.
/// </para>
/// <para>
/// <b>A dismissal holds for one window length, then lapses.</b> By then every sitting still inside the window was
/// made after the dismissal, so a pattern that comes back has re-formed from what the user did since saying
/// "not any more" — they took swimming up again — and is worth offering again. One that has not re-formed simply
/// is not in the view. No expiry job, no "undo" endpoint: the row is inert once lapsed, and a second dismissal
/// refreshes it.
/// </para>
/// </remarks>
public static class SuggestionPatternWindow
{
    /// <summary>A weekday pattern counts sittings from the last 12 weeks.</summary>
    public const int WeeklyWindowDays = 84;

    /// <summary>…and needs one of them in the last 4.</summary>
    public const int WeeklyLastSeenDays = 28;

    /// <summary>A day-of-month pattern counts sittings from the last 12 months — three a year is the floor.</summary>
    public const int MonthlyWindowDays = 365;

    /// <summary>…and needs one of them in the last 3 months.</summary>
    public const int MonthlyLastSeenDays = 92;

    public static int WindowDays(RecurrenceType patternType) => patternType switch
    {
        RecurrenceType.DayOfWeek => WeeklyWindowDays,
        RecurrenceType.DayOfMonth => MonthlyWindowDays,
        _ => throw new ArgumentOutOfRangeException(nameof(patternType), patternType,
            "Only DayOfWeek and DayOfMonth patterns exist.")
    };

    /// <summary>Whether a dismissal made on <paramref name="dismissedOn"/> still hides its pattern on <paramref name="today"/>.</summary>
    public static bool StillHolds(RecurrenceType patternType, DateOnly dismissedOn, DateOnly today) =>
        dismissedOn > today.AddDays(-WindowDays(patternType));
}
