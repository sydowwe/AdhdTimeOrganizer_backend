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
