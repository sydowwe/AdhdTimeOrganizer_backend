using AdhdTimeOrganizer.Tracking.domain.helper.unified;
using Sydowwe.Notifications.application.text;

namespace AdhdTimeOrganizer.infrastructure.notification;

/// <summary>
/// English wording for the kinds this portal raises — sentence for sentence
/// <see cref="SkPortalNotificationTexts"/>, so the same event says the same thing in either language.
/// See that class for where this sits and what guards it.
/// </summary>
public class EnPortalNotificationTexts : EnNotificationTexts
{
    public override string PersonalReminderTitle => "Reminder";
    public override string PersonalReminderFallbackBody => "You have a reminder.";

    public override string RoutinePeriodEndingSoonTitle => "Period ending soon";
    public override string RoutinePeriodEndingSoonFallbackBody => "The period is ending soon and you have unfinished tasks.";

    public override string RoutinePeriodEndingSoonBody(string? periodName, int daysLeft, int remaining)
    {
        var name = periodName is { Length: > 0 } ? Quote(periodName) : "The period";
        return $"{name} ends in {daysLeft} {Plural(daysLeft, "day", "days")}, {remaining} {Plural(remaining, "task", "tasks")} left.";
    }

    public override string ProgressSuffix(int done, int total) => $" Done {done} of {total}.";
    public override string StreakSuffix(int streak) => $" Streak: {streak}.";

    public override string RoutinePeriodEndedTitle => "Period ended";
    public override string RoutinePeriodEndedFallbackBody => "The period has ended.";

    public override string RoutinePeriodEndedBody(string? periodName, int completed, int total)
    {
        var name = periodName is { Length: > 0 } ? Quote(periodName) : "The period";
        return $"{name} has ended: {completed} of {total} done.";
    }

    public override string StreakExtendedSuffix(int streak) => $" Streak continues: {streak}.";
    public override string StreakOnGraceSuffix(int streak) => $" Your streak ({streak}) is holding for now, within the grace period.";
    public override string StreakBrokenSuffix => " The streak was broken.";

    // "Skip" is what the client calls a streak freeze (todoList.en.ts, freeze.*).
    public override string StreakBrokenFreezableSuffix(int lostStreak, int freezesLeft) =>
        $" Your streak of {lostStreak} broke — you have {freezesLeft} {Plural(freezesLeft, "skip", "skips")} left to save it.";

    // Same phrasing as the client's run labels (todoList.en.ts, run* / unit*).
    public override string StreakNewBestSuffix(int streak, int previousBest, int periodLengthInDays) =>
        $" New personal best: {RunLabel(streak, periodLengthInDays)} (previous best: {CountLabel(previousBest, periodLengthInDays)}).";

    public override string StreakMilestoneSuffix(int streak, int periodLengthInDays, bool isNewBest) =>
        isNewBest
            ? $" Milestone: {RunLabel(streak, periodLengthInDays)} — your longest yet!"
            : $" Milestone: {RunLabel(streak, periodLengthInDays)}!";

    /// <summary>"7 weeks in a row".</summary>
    private static string RunLabel(int count, int periodLengthInDays) => $"{CountLabel(count, periodLengthInDays)} in a row";

    /// <summary>"7 weeks".</summary>
    private static string CountLabel(int count, int periodLengthInDays) => RoutineRunUnits.For(periodLengthInDays) switch
    {
        RoutineRunUnit.Day => $"{count} {Plural(count, "day", "days")}",
        RoutineRunUnit.Week => $"{count} {Plural(count, "week", "weeks")}",
        RoutineRunUnit.Month => $"{count} {Plural(count, "month", "months")}",
        _ => $"{count} {Plural(count, "period", "periods")}"
    };

    public override string RoutineStreakGraceExpiringTitle => "Streak at risk";
    public override string RoutineStreakGraceExpiringFallbackBody => "The grace period for your streak is ending soon.";

    public override string RoutineStreakGraceExpiringBody(string? periodName, int streak, DateTimeOffset? graceUntil)
    {
        var name = periodName is { Length: > 0 } ? Quote(periodName) : "the period";
        var until = graceUntil is { } at ? $" by {FormatInstant(at)}" : string.Empty;
        return $"Your streak of {streak} for {name} will break unless you finish it{until}.";
    }

    public override string RoutineSuggestedTodayTitle => "Suggested for today";
    public override string RoutineSuggestedTodayFallbackBody => "You have routine tasks suggested for today.";

    // "Gym", "Gym and Pay rent", "Gym, Pay rent and Dentist", "Gym, Pay rent and 2 more".
    public override string RoutineSuggestedTodayBody(IReadOnlyList<string> names, int more)
    {
        List<string> parts = more > 0 ? [.. names, $"{more} more"] : [.. names];
        return parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts[..^1])} and {parts[^1]}";
    }

    // "Due in 30 min", "… in 2 h", "… in 1 h 30 min"; "Due now"; no lead: "Due soon".
    public override string TodoItemDueTitle(int? minutesBefore) => minutesBefore switch
    {
        null => "Due soon",
        <= 0 => "Due now",
        var m => $"Due in {Lead(m.Value)}"
    };

    public override string TodoItemDueFallbackBody => "One of your to-dos is coming due.";

    // "Starts in 10 min", "… in 1 h 30 min"; "Starting now"; no lead: "Starting soon".
    public override string PlannedBlockStartingTitle(int? minutesBefore) => minutesBefore switch
    {
        null => "Starting soon",
        <= 0 => "Starting now",
        var m => $"Starts in {Lead(m.Value)}"
    };

    public override string PlannedBlockStartingFallbackBody => "Your next planned block is about to start.";

    // A nudge, not a reproach: it says what happened and offers the next step.
    public override string PlannedBlocksMissedTitle => "Your plan slipped";

    // "1 planned block slipped by — re-plan the rest of today?", "3 planned blocks slipped by — …".
    public override string PlannedBlocksMissedBody(int count) =>
        $"{count} planned {Plural(count, "block", "blocks")} slipped by — re-plan the rest of today?";

    public override string PlannedBlocksMissedFallbackBody => "Some planned blocks slipped by — re-plan the rest of today?";

    public override string PlannedBlockOverrunTitle => "Time's up";

    // "Time for “Deep work” ran out 10 min ago — wrap up or extend?"; no name: "Your planned block ended …".
    public override string PlannedBlockOverrunBody(string? activityName, int minutesOver)
    {
        var ago = minutesOver > 0 ? $" {Lead(minutesOver)} ago" : string.Empty;
        return activityName is { Length: > 0 }
            ? $"Time for {Quote(activityName)} ran out{ago} — wrap up or extend?"
            : $"Your planned block ended{ago} — wrap up or extend?";
    }

    public override string MorningBriefingTitle => "Your day";
    public override string MorningBriefingFallbackBody => "Here's what today holds.";

    // "4 blocks planned, first at 09:00 · 2 to carry over · 3 to-dos due today · 1 overdue · 2 routine tasks for today".
    public override string MorningBriefingPlan(int blocks, TimeOnly? firstAt)
    {
        var plan = $"{blocks} {Plural(blocks, "block", "blocks")} planned";
        return firstAt is { } at ? $"{plan}, first at {at:HH\\:mm}" : plan;
    }

    // Only ever sent next to something else, so it reads as a gentle prompt, not an empty report.
    public override string MorningBriefingNoPlan => "No plan yet";
    public override string MorningBriefingCarryOver(int count) => $"{count} to carry over";
    public override string MorningBriefingDueToday(int count) => $"{count} {Plural(count, "to-do", "to-dos")} due today";
    public override string MorningBriefingOverdue(int count) => $"{count} overdue";
    public override string MorningBriefingRoutines(int count) => $"{count} routine {Plural(count, "task", "tasks")} for today";
    public override string MorningBriefingBody(IReadOnlyList<string> sections) => string.Join(" · ", sections);

    public override string TrackerSilentTitle => "Tracking went quiet";

    public override string TrackerSilentFallbackBody =>
        "One of your trackers has stopped sending your activity. Check that it's running and signed in.";

    public override string TrackerSilentBody(string source) => source switch
    {
        TrackingSourceNames.Desktop =>
            "The desktop app has stopped sending your activity. Check that it's running and signed in.",
        TrackingSourceNames.WebExtension =>
            "The browser extension has stopped sending your activity. Check that it's enabled and signed in.",
        TrackingSourceNames.Android =>
            "The phone app has stopped sending your activity. Check that it's installed and signed in.",
        _ => TrackerSilentFallbackBody
    };

    private static string Lead(int minutes) => (minutes / 60, minutes % 60) switch
    {
        (0, var m) => $"{m} min",
        (var h, 0) => $"{h} h",
        var (h, m) => $"{h} h {m} min"
    };
}
