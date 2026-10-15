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

    private static string Lead(int minutes) => (minutes / 60, minutes % 60) switch
    {
        (0, var m) => $"{m} min",
        (var h, 0) => $"{h} h",
        var (h, m) => $"{h} h {m} min"
    };
}
