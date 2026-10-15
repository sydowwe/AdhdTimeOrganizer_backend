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
    // "Due in 30 min", "… in 2 h", "… in 1 h 30 min"; "Due now"; no lead: "Due soon".
    public override string TodoItemDueTitle(int? minutesBefore) => minutesBefore switch
    {
        null => "Due soon",
        <= 0 => "Due now",
        var m => $"Due in {Lead(m.Value)}"
    };

    public override string TodoItemDueFallbackBody => "One of your to-dos is coming due.";

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
