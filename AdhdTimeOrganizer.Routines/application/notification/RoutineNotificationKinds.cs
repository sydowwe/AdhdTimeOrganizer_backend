using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.kind;
using Sydowwe.Framework.Contracts.notification.text;
using Sydowwe.Framework.domain.@enum;

namespace AdhdTimeOrganizer.Routines.application.notification;

// All four routine kinds are unsolicited and about a period that passes without them, so a user away three days or more
// (RecipientEngagementReader) finds them in the bell instead of on their lock screen. None is mailed by default: they
// recur on every period of every routine the user keeps, so e-mail default-on would be the noisiest thing in the app;
// they are opt-in per period already (RoutineTimePeriod.ReminderLeadDays), and a user who wants the mail ticks it.

/// <summary>
/// A <c>RoutineTimePeriod</c> is about to reset with items still unfinished — the lead-time nudge, fired
/// <c>ReminderLeadDays</c> before the reset by a daily sweep (the counts must be live at nudge time, and a reminder
/// payload is frozen at registration). Leads with what is still outstanding, the number the user acts on, with the
/// progress and streak added only when there is something to say.
/// </summary>
public sealed class RoutinePeriodEndingSoonNotificationKind : NotificationKind<RoutinePeriodEndingSoonPayload>, ISingletonService
{
    public override NotificationType Type => RoutinesNotificationTypes.RoutinePeriodEndingSoon;

    public override bool HeldWhileAway => true;

    protected override (string Title, string Body) Render(RoutinePeriodEndingSoonPayload? payload, NotificationRenderContext context)
    {
        var kit = context.Kit;
        if (kit.Language == AvailableLocales.En)
        {
            if (payload is null)
                return ("Period ending soon", "The period is ending soon and you have unfinished tasks.");

            var name = NonEmpty(payload.PeriodName) is { } n ? kit.Quote(n) : "The period";
            var progress = payload.Total > 0 ? $" Done {payload.Total - payload.Remaining} of {payload.Total}." : string.Empty;
            var streak = payload.Streak > 0 ? $" Streak: {payload.Streak}." : string.Empty;
            return ("Period ending soon",
                $"{name} ends in {payload.DaysLeft} {EnNotificationTextKit.Plural(payload.DaysLeft, "day", "days")}, " +
                $"{payload.Remaining} {EnNotificationTextKit.Plural(payload.Remaining, "task", "tasks")} left.{progress}{streak}");
        }
        else
        {
            if (payload is null)
                return ("Obdobie sa čoskoro končí", "Obdobie sa čoskoro končí a máte nedokončené úlohy.");

            var name = NonEmpty(payload.PeriodName) is { } n ? kit.Quote(n) : "Obdobie";
            var days = $"{payload.DaysLeft} {SkNotificationTextKit.Plural(payload.DaysLeft, "deň", "dni", "dní")}";
            // The verb agrees with the count too: "zostáva 1 úloha", "zostávajú 2 úlohy", "zostáva 5 úloh".
            var left = $"{SkNotificationTextKit.Plural(payload.Remaining, "zostáva", "zostávajú", "zostáva")} {payload.Remaining} " +
                       SkNotificationTextKit.Plural(payload.Remaining, "úloha", "úlohy", "úloh");
            var progress = payload.Total > 0 ? $" Hotové {payload.Total - payload.Remaining} z {payload.Total}." : string.Empty;
            var streak = payload.Streak > 0 ? $" Séria: {payload.Streak}." : string.Empty;
            return ("Obdobie sa čoskoro končí", $"{name} sa končí o {days}, {left}.{progress}{streak}");
        }
    }

    protected override NotificationSubject? Subject(RoutinePeriodEndingSoonPayload? payload) => RoutinePeriodSubject.Of(payload?.PeriodId);
}

/// <summary>
/// A <c>RoutineTimePeriod</c> just reset: how much got done, and what that did to the streak. Reactive, not scheduled —
/// raised by the reset job at the moment it clears the items.
/// <para>
/// The streak outcome picks the closing sentence; <see cref="RoutineStreakOutcome.Unknown"/> is the shape-drift fallback
/// and says nothing about the streak rather than guessing. An extended streak gets one closing sentence, never two: a
/// milestone outranks a new best and folds it in. The freeze is offered on a broken one only when the producer checked
/// it would work now; an older row falls back to the plain sentence rather than promising a rescue.
/// </para>
/// </summary>
public sealed class RoutinePeriodEndedNotificationKind : NotificationKind<RoutinePeriodEndedPayload>, ISingletonService
{
    public override NotificationType Type => RoutinesNotificationTypes.RoutinePeriodEnded;

    public override bool HeldWhileAway => true;

    protected override (string Title, string Body) Render(RoutinePeriodEndedPayload? payload, NotificationRenderContext context)
    {
        var w = context.Kit.Language == AvailableLocales.En ? (PeriodEndedWords)new EnPeriodEndedWords(context.Kit) : new SkPeriodEndedWords(context.Kit);
        if (payload is null)
            return (w.Title, w.Fallback);

        var outcome = payload.Outcome switch
        {
            RoutineStreakOutcome.Extended when payload.IsMilestone
                => w.Milestone(payload.Streak, payload.PeriodLengthInDays, payload.IsNewBest),
            RoutineStreakOutcome.Extended when payload is { IsNewBest: true, PreviousBest: > 0 }
                => w.NewBest(payload.Streak, payload.PreviousBest, payload.PeriodLengthInDays),
            RoutineStreakOutcome.Extended => w.Extended(payload.Streak),
            RoutineStreakOutcome.OnGrace => w.OnGrace(payload.Streak),
            RoutineStreakOutcome.Broken when payload is { CanFreeze: true, LostStreak: > 0, FreezesRemaining: > 0 }
                => w.BrokenFreezable(payload.LostStreak, payload.FreezesRemaining),
            RoutineStreakOutcome.Broken => w.Broken,
            _ => string.Empty
        };

        return (w.Title, $"{w.Body(NonEmpty(payload.PeriodName), payload.CompletedCount, payload.TotalCount)}{outcome}");
    }

    protected override NotificationSubject? Subject(RoutinePeriodEndedPayload? payload) => RoutinePeriodSubject.Of(payload?.PeriodId);

    /// <summary>
    /// The sentences, per language. Fragments returned with a leading space are appended to the body and carry their own
    /// separator. A run is counted in its own unit (<see cref="RoutineRunUnits"/>) — the client's run labels' phrasing.
    /// </summary>
    private abstract record PeriodEndedWords(NotificationTextKit Kit)
    {
        public abstract string Title { get; }
        public abstract string Fallback { get; }
        public abstract string Body(string? periodName, int completed, int total);
        public abstract string Extended(int streak);
        public abstract string OnGrace(int streak);
        public abstract string Broken { get; }
        public abstract string BrokenFreezable(int lostStreak, int freezesLeft);
        public abstract string NewBest(int streak, int previousBest, int periodLengthInDays);
        public abstract string Milestone(int streak, int periodLengthInDays, bool isNewBest);
    }

    private sealed record SkPeriodEndedWords(NotificationTextKit Kit) : PeriodEndedWords(Kit)
    {
        public override string Title => "Obdobie sa skončilo";
        public override string Fallback => "Obdobie sa skončilo.";

        public override string Body(string? periodName, int completed, int total) =>
            $"{(periodName is { } n ? Kit.Quote(n) : "Obdobie")} sa skončilo: hotové {completed} z {total}.";

        public override string Extended(int streak) => $" Séria pokračuje: {streak}.";
        public override string OnGrace(int streak) => $" Séria ({streak}) je zatiaľ zachovaná v odkladovej lehote.";
        public override string Broken => " Séria bola prerušená.";

        // "Vynechanie" is what the client calls a streak freeze (todoList.sk.ts, freeze.*), verb agreeing as there.
        public override string BrokenFreezable(int lostStreak, int freezesLeft) =>
            $" Séria {lostStreak} sa prerušila — na jej záchranu vám {Plural(freezesLeft, "zostáva", "zostávajú", "zostáva")} " +
            $"{freezesLeft} {Plural(freezesLeft, "vynechanie", "vynechania", "vynechaní")}.";

        // Same phrasing as the client's run labels (todoList.sk.ts, run* / unit*), but in the formal "vy" register every
        // notification here uses, where the client says "tvoja".
        public override string NewBest(int streak, int previousBest, int periodLengthInDays) =>
            $" Nový osobný rekord: {Run(streak, periodLengthInDays)} (doterajší rekord: {Count(previousBest, periodLengthInDays)}).";

        public override string Milestone(int streak, int periodLengthInDays, bool isNewBest) => isNewBest
            ? $" Míľnik: {Run(streak, periodLengthInDays)} — vaša doteraz najdlhšia séria!"
            : $" Míľnik: {Run(streak, periodLengthInDays)}!";

        /// <summary>"7 týždňov po sebe".</summary>
        private static string Run(int count, int periodLengthInDays) => $"{Count(count, periodLengthInDays)} po sebe";

        /// <summary>"7 týždňov".</summary>
        private static string Count(int count, int periodLengthInDays) => RoutineRunUnits.For(periodLengthInDays) switch
        {
            RoutineRunUnit.Day => $"{count} {Plural(count, "deň", "dni", "dní")}",
            RoutineRunUnit.Week => $"{count} {Plural(count, "týždeň", "týždne", "týždňov")}",
            RoutineRunUnit.Month => $"{count} {Plural(count, "mesiac", "mesiace", "mesiacov")}",
            _ => $"{count} {Plural(count, "obdobie", "obdobia", "období")}"
        };

        private static string Plural(int count, string one, string few, string many) => SkNotificationTextKit.Plural(count, one, few, many);
    }

    private sealed record EnPeriodEndedWords(NotificationTextKit Kit) : PeriodEndedWords(Kit)
    {
        public override string Title => "Period ended";
        public override string Fallback => "The period has ended.";

        public override string Body(string? periodName, int completed, int total) =>
            $"{(periodName is { } n ? Kit.Quote(n) : "The period")} has ended: {completed} of {total} done.";

        public override string Extended(int streak) => $" Streak continues: {streak}.";
        public override string OnGrace(int streak) => $" Your streak ({streak}) is holding for now, within the grace period.";
        public override string Broken => " The streak was broken.";

        // "Skip" is what the client calls a streak freeze (todoList.en.ts, freeze.*).
        public override string BrokenFreezable(int lostStreak, int freezesLeft) =>
            $" Your streak of {lostStreak} broke — you have {freezesLeft} {Plural(freezesLeft, "skip", "skips")} left to save it.";

        // Same phrasing as the client's run labels (todoList.en.ts, run* / unit*).
        public override string NewBest(int streak, int previousBest, int periodLengthInDays) =>
            $" New personal best: {Run(streak, periodLengthInDays)} (previous best: {Count(previousBest, periodLengthInDays)}).";

        public override string Milestone(int streak, int periodLengthInDays, bool isNewBest) => isNewBest
            ? $" Milestone: {Run(streak, periodLengthInDays)} — your longest yet!"
            : $" Milestone: {Run(streak, periodLengthInDays)}!";

        /// <summary>"7 weeks in a row".</summary>
        private static string Run(int count, int periodLengthInDays) => $"{Count(count, periodLengthInDays)} in a row";

        /// <summary>"7 weeks".</summary>
        private static string Count(int count, int periodLengthInDays) => RoutineRunUnits.For(periodLengthInDays) switch
        {
            RoutineRunUnit.Day => $"{count} {Plural(count, "day", "days")}",
            RoutineRunUnit.Week => $"{count} {Plural(count, "week", "weeks")}",
            RoutineRunUnit.Month => $"{count} {Plural(count, "month", "months")}",
            _ => $"{count} {Plural(count, "period", "periods")}"
        };

        private static string Plural(int count, string one, string many) => EnNotificationTextKit.Plural(count, one, many);
    }
}

/// <summary>
/// A period's streak grace window (<c>StreakGraceUntil</c>) is about to lapse. Fires mid-period, between two resets, so
/// it names what is at stake and by when — the deadline is the actionable half.
/// </summary>
public sealed class RoutineStreakGraceExpiringNotificationKind : NotificationKind<RoutineStreakGraceExpiringPayload>, ISingletonService
{
    public override NotificationType Type => RoutinesNotificationTypes.RoutineStreakGraceExpiring;

    public override bool HeldWhileAway => true;

    protected override (string Title, string Body) Render(RoutineStreakGraceExpiringPayload? payload, NotificationRenderContext context)
    {
        var kit = context.Kit;
        var en = kit.Language == AvailableLocales.En;
        var title = en ? "Streak at risk" : "Séria je ohrozená";
        if (payload is null)
            return (title, en ? "The grace period for your streak is ending soon." : "Odkladová lehota pre vašu sériu sa čoskoro končí.");

        DateTimeOffset? graceUntil = payload.GraceUntil;
        var periodName = NonEmpty(payload.PeriodName);
        if (en)
        {
            var name = periodName is { } n ? kit.Quote(n) : "the period";
            var until = graceUntil is { } at ? $" by {kit.FormatInstant(at)}" : string.Empty;
            return (title, $"Your streak of {payload.Streak} for {name} will break unless you finish it{until}.");
        }
        else
        {
            var name = periodName is { } n ? kit.Quote(n) : "obdobie";
            var until = graceUntil is { } at ? $" do {kit.FormatInstant(at)}" : string.Empty;
            return (title, $"Séria {payload.Streak} pre {name} sa preruší, ak ju nedokončíte{until}.");
        }
    }

    protected override NotificationSubject? Subject(RoutineStreakGraceExpiringPayload? payload) => RoutinePeriodSubject.Of(payload?.PeriodId);
}

/// <summary>
/// Once-a-day digest of the routine items suggested for the user's today (a weekly item's <c>SuggestedDays</c>, a
/// monthly item's <c>SuggestedDayOfMonth</c>) and not done yet. One notification per user per day, never one per item,
/// and nothing at all when nothing qualifies. Composed by a sweep at send time, so an item ticked minutes before the
/// morning run is not named in it.
/// <para>
/// The body spells out up to <see cref="ListedNames"/> names; past that it lists one fewer and counts the rest, so
/// "and 1 more" — a name's worth of space spent not saying the name — never renders. An item whose name is missing
/// still counts towards "more".
/// </para>
/// </summary>
public sealed class RoutineSuggestedTodayNotificationKind : NotificationKind<RoutineSuggestedTodayPayload>, ISingletonService
{
    private const int ListedNames = 3;

    public override NotificationType Type => RoutinesNotificationTypes.RoutineSuggestedToday;

    public override bool HeldWhileAway => true;

    protected override (string Title, string Body) Render(RoutineSuggestedTodayPayload? payload, NotificationRenderContext context)
    {
        var en = context.Kit.Language == AvailableLocales.En;
        var title = en ? "Suggested for today" : "Na dnes odporúčané";

        var names = payload?.Items?.Select(i => i.Name).OfType<string>().Where(n => n.Length > 0).ToList() ?? [];
        if (names.Count == 0)
            return (title, en ? "You have routine tasks suggested for today." : "Na dnes máte odporúčané úlohy z rutín.");

        var total = Math.Max(payload!.Count, payload.Items!.Count);
        var listed = names.Take(total <= ListedNames ? ListedNames : ListedNames - 1).ToList();
        var more = total - listed.Count;

        // "Gym, Pay rent and 2 more" / "Posilňovňa, Nájom a ďalšie 2" / "… a ďalších 5".
        List<string> parts = more > 0
            ? [.. listed, en ? $"{more} more" : $"{SkNotificationTextKit.Plural(more, "ďalšia", "ďalšie", "ďalších")} {more}"]
            : [.. listed];
        var and = en ? "and" : "a";
        return (title, parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts[..^1])} {and} {parts[^1]}");
    }

    /// <summary>
    /// One period is a place to open; items spread over several are not, and there is deliberately no per-item kind —
    /// the client falls back to the type's own route, the whole routine list.
    /// </summary>
    protected override NotificationSubject? Subject(RoutineSuggestedTodayPayload? payload) =>
        RoutinePeriodSubject.Of(payload?.Items?.Select(i => i.PeriodId).Distinct().ToList() is [> 0 and var periodId] ? periodId : null);
}

/// <summary>The routine period a notification links to.</summary>
internal static class RoutinePeriodSubject
{
    public static NotificationSubject? Of(long? periodId) =>
        periodId is { } id ? new NotificationSubject(NotificationSubjectKinds.RoutinePeriod, id) : null;
}
