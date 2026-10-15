using AdhdTimeOrganizer.Tracking.domain.helper.unified;
using Sydowwe.Notifications.application.text;

namespace AdhdTimeOrganizer.infrastructure.notification;

/// <summary>
/// Slovak wording for the notification kinds <b>this portal</b> raises — the routine nudges (Routines
/// slice), the pomodoro boundary (History slice) and the self-set reminder (Planning slice). The framework's
/// <see cref="SkNotificationTexts"/> carries only the kinds its own modules raise; a kind an app raises is
/// worded by that app, or it renders the generic "Nová notifikácia" (backend ask B15).
/// <para>
/// Lives in the host, not a slice, for the same reason the catalog does: one table per language, and the
/// composition root is the one place that sees every slice. Registered next to
/// <c>AddNotificationCatalog</c> in <c>ModuleServiceExtensions</c>; <c>NotificationVocabularyTests</c> renders
/// every catalogued kind and refuses the generic fallback, so adding a kind to the catalog without wording
/// it here is a red test rather than a bland notification.
/// </para>
/// </summary>
public class SkPortalNotificationTexts : SkNotificationTexts
{
    // "Termín úlohy o 30 min", "… o 2 h", "… o 1 h 30 min"; "Termín úlohy je teraz"; no lead: "Termín úlohy sa blíži".
    public override string TodoItemDueTitle(int? minutesBefore) => minutesBefore switch
    {
        null => "Termín úlohy sa blíži",
        <= 0 => "Termín úlohy je teraz",
        var m => $"Termín úlohy o {Lead(m.Value)}"
    };

    public override string TodoItemDueFallbackBody => "Blíži sa termín jednej z vašich úloh.";

    public override string MorningBriefingTitle => "Váš deň";
    public override string MorningBriefingFallbackBody => "Toto vás dnes čaká.";

    // "4 naplánované bloky, prvý o 09:00 · 2 na presunutie · 3 úlohy s termínom dnes · 1 po termíne · 2 úlohy z rutín na dnes".
    public override string MorningBriefingPlan(int blocks, TimeOnly? firstAt)
    {
        var plan = $"{blocks} {Plural(blocks, "naplánovaný blok", "naplánované bloky", "naplánovaných blokov")}";
        return firstAt is { } at ? $"{plan}, prvý o {at:HH\\:mm}" : plan;
    }

    // Only ever sent next to something else, so it reads as a gentle prompt, not an empty report.
    public override string MorningBriefingNoPlan => "Zatiaľ bez plánu";
    public override string MorningBriefingCarryOver(int count) => $"{count} na presunutie";
    public override string MorningBriefingDueToday(int count) => $"{count} {Plural(count, "úloha", "úlohy", "úloh")} s termínom dnes";
    public override string MorningBriefingOverdue(int count) => $"{count} po termíne";
    public override string MorningBriefingRoutines(int count) => $"{count} {Plural(count, "úloha", "úlohy", "úloh")} z rutín na dnes";
    public override string MorningBriefingBody(IReadOnlyList<string> sections) => string.Join(" · ", sections);

    // Operational, not a reproach: what stopped, and the two things that usually fixed it. No "for 2 days" — the
    // payload carries the last report as an instant, and only the client knows the user's zone to word it.
    public override string TrackerSilentTitle => "Sledovanie aktivity stíchlo";

    public override string TrackerSilentFallbackBody =>
        "Jeden z vašich sledovačov prestal posielať aktivitu. Skontrolujte, či beží a či ste v ňom prihlásení.";

    public override string TrackerSilentBody(string source) => source switch
    {
        TrackingSourceNames.Desktop =>
            "Desktopová aplikácia prestala posielať vašu aktivitu. Skontrolujte, či beží a či ste v nej prihlásení.",
        TrackingSourceNames.WebExtension =>
            "Rozšírenie prehliadača prestalo posielať vašu aktivitu. Skontrolujte, či je zapnuté a či ste v ňom prihlásení.",
        TrackingSourceNames.Android =>
            "Aplikácia v telefóne prestala posielať vašu aktivitu. Skontrolujte, či je nainštalovaná a či ste v nej prihlásení.",
        _ => TrackerSilentFallbackBody
    };

    private static string Lead(int minutes) => (minutes / 60, minutes % 60) switch
    {
        (0, var m) => $"{m} min",
        (var h, 0) => $"{h} h",
        var (h, m) => $"{h} h {m} min"
    };
}
