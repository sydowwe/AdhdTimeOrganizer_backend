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
    public override string PersonalReminderTitle => "Pripomienka";
    public override string PersonalReminderFallbackBody => "Máte pripomienku.";

    public override string RoutinePeriodEndingSoonTitle => "Obdobie sa čoskoro končí";
    public override string RoutinePeriodEndingSoonFallbackBody => "Obdobie sa čoskoro končí a máte nedokončené úlohy.";

    public override string RoutinePeriodEndingSoonBody(string? periodName, int daysLeft, int remaining)
    {
        var name = periodName is { Length: > 0 } ? Quote(periodName) : "Obdobie";
        var days = $"{daysLeft} {Plural(daysLeft, "deň", "dni", "dní")}";
        // The verb agrees with the count too: "zostáva 1 úloha", "zostávajú 2 úlohy", "zostáva 5 úloh".
        var left = $"{Plural(remaining, "zostáva", "zostávajú", "zostáva")} {remaining} {Plural(remaining, "úloha", "úlohy", "úloh")}";
        return $"{name} sa končí o {days}, {left}.";
    }

    public override string ProgressSuffix(int done, int total) => $" Hotové {done} z {total}.";
    public override string StreakSuffix(int streak) => $" Séria: {streak}.";

    public override string RoutinePeriodEndedTitle => "Obdobie sa skončilo";
    public override string RoutinePeriodEndedFallbackBody => "Obdobie sa skončilo.";

    public override string RoutinePeriodEndedBody(string? periodName, int completed, int total)
    {
        var name = periodName is { Length: > 0 } ? Quote(periodName) : "Obdobie";
        return $"{name} sa skončilo: hotové {completed} z {total}.";
    }

    public override string StreakExtendedSuffix(int streak) => $" Séria pokračuje: {streak}.";
    public override string StreakOnGraceSuffix(int streak) => $" Séria ({streak}) je zatiaľ zachovaná v odkladovej lehote.";
    public override string StreakBrokenSuffix => " Séria bola prerušená.";

    // "Vynechanie" is what the client calls a streak freeze (todoList.sk.ts, freeze.*), verb agreeing as there.
    public override string StreakBrokenFreezableSuffix(int lostStreak, int freezesLeft) =>
        $" Séria {lostStreak} sa prerušila — na jej záchranu vám {Plural(freezesLeft, "zostáva", "zostávajú", "zostáva")} " +
        $"{freezesLeft} {Plural(freezesLeft, "vynechanie", "vynechania", "vynechaní")}.";

    public override string RoutineStreakGraceExpiringTitle => "Séria je ohrozená";
    public override string RoutineStreakGraceExpiringFallbackBody => "Odkladová lehota pre vašu sériu sa čoskoro končí.";

    public override string RoutineStreakGraceExpiringBody(string? periodName, int streak, DateTimeOffset? graceUntil)
    {
        var name = periodName is { Length: > 0 } ? Quote(periodName) : "obdobie";
        var until = graceUntil is { } at ? $" do {FormatInstant(at)}" : string.Empty;
        return $"Séria {streak} pre {name} sa preruší, ak ju nedokončíte{until}.";
    }

    public override string RoutineSuggestedTodayTitle => "Na dnes odporúčané";
    public override string RoutineSuggestedTodayFallbackBody => "Na dnes máte odporúčané úlohy z rutín.";

    // "Posilňovňa", "Posilňovňa a Nájom", "Posilňovňa, Nájom a Zubár", "Posilňovňa, Nájom a ďalšie 2" / "a ďalších 5".
    public override string RoutineSuggestedTodayBody(IReadOnlyList<string> names, int more)
    {
        List<string> parts = more > 0 ? [.. names, $"{Plural(more, "ďalšia", "ďalšie", "ďalších")} {more}"] : [.. names];
        return parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts[..^1])} a {parts[^1]}";
    }

    // "Termín úlohy o 30 min", "… o 2 h", "… o 1 h 30 min"; "Termín úlohy je teraz"; no lead: "Termín úlohy sa blíži".
    public override string TodoItemDueTitle(int? minutesBefore) => minutesBefore switch
    {
        null => "Termín úlohy sa blíži",
        <= 0 => "Termín úlohy je teraz",
        var m => $"Termín úlohy o {Lead(m.Value)}"
    };

    public override string TodoItemDueFallbackBody => "Blíži sa termín jednej z vašich úloh.";

    // "Začína o 10 min", "… o 1 h 30 min"; "Začína teraz"; no lead: "Čoskoro začína".
    public override string PlannedBlockStartingTitle(int? minutesBefore) => minutesBefore switch
    {
        null => "Čoskoro začína",
        <= 0 => "Začína teraz",
        var m => $"Začína o {Lead(m.Value)}"
    };

    public override string PlannedBlockStartingFallbackBody => "Váš ďalší naplánovaný blok čoskoro začína.";

    // A nudge, not a reproach: it says what happened and offers the next step.
    public override string PlannedBlocksMissedTitle => "Plán sa posunul";

    // Noun, adjective and verb all agree with the count: "1 naplánovaný blok ušiel", "2 naplánované bloky ušli",
    // "5 naplánovaných blokov ušlo".
    public override string PlannedBlocksMissedBody(int count) =>
        $"{count} {Plural(count, "naplánovaný blok ušiel", "naplánované bloky ušli", "naplánovaných blokov ušlo")} — preplánovať zvyšok dňa?";

    public override string PlannedBlocksMissedFallbackBody => "Niektoré naplánované bloky ušli — preplánovať zvyšok dňa?";

    public override string PlannedBlockOverrunTitle => "Čas vypršal";

    // "Čas na „Deep work“ vypršal pred 10 min — ukončiť, alebo predĺžiť?"; no name: "Váš naplánovaný blok skončil …".
    public override string PlannedBlockOverrunBody(string? activityName, int minutesOver)
    {
        var ago = minutesOver > 0 ? $" pred {Lead(minutesOver)}" : string.Empty;
        return activityName is { Length: > 0 }
            ? $"Čas na {Quote(activityName)} vypršal{ago} — ukončiť, alebo predĺžiť?"
            : $"Váš naplánovaný blok skončil{ago} — ukončiť, alebo predĺžiť?";
    }

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

    public override string TimerEndedTitle => "Časovač skončil";
    public override string FocusEndedTitle => "Sústredenie skončilo";
    public override string ShortBreakEndedTitle => "Krátka prestávka skončila";
    public override string LongBreakEndedTitle => "Dlhá prestávka skončila";

    public override string CountdownEndedLead => "Odpočet skončil.";
    public override string TimeForBreakLead => "Čas na prestávku!";
    public override string BackToWorkLead => "Späť do práce!";

    public override string Phase(int index, int? total) => total is { } t ? $"Fáza {index}/{t}" : $"Fáza {index}";
    public override string Cycle(int index, int? total) => total is { } t ? $"Cyklus {index}/{t}" : $"Cyklus {index}";
}
