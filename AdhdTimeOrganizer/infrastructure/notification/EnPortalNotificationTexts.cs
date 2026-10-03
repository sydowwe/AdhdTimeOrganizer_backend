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

    public override string TimerEndedTitle => "Timer finished";
    public override string FocusEndedTitle => "Focus finished";
    public override string ShortBreakEndedTitle => "Short break over";
    public override string LongBreakEndedTitle => "Long break over";

    public override string CountdownEndedLead => "The countdown is over.";
    public override string TimeForBreakLead => "Time for a break!";
    public override string BackToWorkLead => "Back to work!";

    public override string Phase(int index, int? total) => total is { } t ? $"Phase {index}/{t}" : $"Phase {index}";
    public override string Cycle(int index, int? total) => total is { } t ? $"Cycle {index}/{t}" : $"Cycle {index}";
}
