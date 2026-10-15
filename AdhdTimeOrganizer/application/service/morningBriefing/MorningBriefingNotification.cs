using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.kind;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Contracts.notification.text;
using Sydowwe.Framework.domain.@enum;

namespace AdhdTimeOrganizer.application.service.morningBriefing;

/// <summary>
/// The notification kinds the host itself raises. The key is stored and on the wire, and is what the client's
/// <c>notificationTypeMeta</c> is keyed on: <b>only add, never rename</b>.
/// </summary>
public static class PortalNotificationTypes
{
    public const string MorningBriefingKey = "MorningBriefing";

    public static readonly NotificationType MorningBriefing = new(MorningBriefingKey);
}

/// <summary>
/// The user's morning, counted — no names, no titles, so nothing user-authored travels in it. <c>Date</c> is the user's
/// own day the briefing is about. Property names are the persisted JSON keys — read <see cref="INotificationPayload"/>.
/// <list type="bullet">
/// <item><c>Blocks</c>: today's open, foreground planned blocks; 0 is "no plan yet".</item>
/// <item><c>FirstBlockAt</c>: the start of the first of them still ahead of the briefing, on the user's wall clock;
/// null when none is.</item>
/// <item><c>CarryOver</c>: blocks left open on the days before today, the carry-over banner's set.</item>
/// <item><c>DueToday</c>: open to-dos due today with no due time (a timed one has its own reminder).</item>
/// <item><c>Overdue</c>: open to-dos due before today.</item>
/// <item><c>RoutinesSuggested</c>: open routine items suggested for today.</item>
/// </list>
/// </summary>
[NotificationPayload(PortalNotificationTypes.MorningBriefingKey)]
public sealed record MorningBriefingPayload(
    DateOnly Date,
    int Blocks = 0,
    TimeOnly? FirstBlockAt = null,
    int CarryOver = 0,
    int DueToday = 0,
    int Overdue = 0,
    int RoutinesSuggested = 0) : INotificationPayload;

/// <summary>
/// The user's morning in one notification: today's plan, the blocks left open on earlier days, the to-dos due today
/// and overdue, and the routine items suggested for today — counts only, sent once per user per day at their own
/// wake-up by <see cref="MorningBriefingNotifier"/>. One summary instead of a push per signal, because for the people
/// this is built for a stream of morning pings is how every channel ends up switched off. Composed at send time and
/// never sent when there is nothing to say.
/// <para>
/// The body leads with the plan — "no plan yet" when there is none, which the producer only sends alongside something
/// else to say — then each non-zero section in a fixed order. A payload that says nothing at all (empty, drifted, or
/// every count zero) gets the name-less sentence rather than a lone "no plan yet".
/// </para>
/// </summary>
public sealed class MorningBriefingNotificationKind : NotificationKind<MorningBriefingPayload>, ISingletonService
{
    public override NotificationType Type => PortalNotificationTypes.MorningBriefing;

    /// <summary>
    /// Unsolicited and about a morning that passes without it. A returning user gets the welcome-back card instead
    /// (<c>MorningBriefingJobHandler</c> skips them while it is open). Not mailed: a daily mail is noise, and a user
    /// who wants one can opt in.
    /// </summary>
    public override bool HeldWhileAway => true;

    protected override (string Title, string Body) Render(MorningBriefingPayload? payload, NotificationRenderContext context)
    {
        var en = context.Kit.Language == AvailableLocales.En;
        var title = en ? "Your day" : "Váš deň";
        var fallback = en ? "Here's what today holds." : "Toto vás dnes čaká.";
        if (payload is null)
            return (title, fallback);

        // "4 naplánované bloky, prvý o 09:00 · 2 na presunutie · 3 úlohy s termínom dnes · 1 po termíne · 2 úlohy z rutín na dnes".
        var sections = new List<string>();
        if (payload.CarryOver > 0)
            sections.Add(en ? $"{payload.CarryOver} to carry over" : $"{payload.CarryOver} na presunutie");
        if (payload.DueToday > 0)
            sections.Add(en
                ? $"{payload.DueToday} {EnNotificationTextKit.Plural(payload.DueToday, "to-do", "to-dos")} due today"
                : $"{payload.DueToday} {SkNotificationTextKit.Plural(payload.DueToday, "úloha", "úlohy", "úloh")} s termínom dnes");
        if (payload.Overdue > 0)
            sections.Add(en ? $"{payload.Overdue} overdue" : $"{payload.Overdue} po termíne");
        if (payload.RoutinesSuggested > 0)
            sections.Add(en
                ? $"{payload.RoutinesSuggested} routine {EnNotificationTextKit.Plural(payload.RoutinesSuggested, "task", "tasks")} for today"
                : $"{payload.RoutinesSuggested} {SkNotificationTextKit.Plural(payload.RoutinesSuggested, "úloha", "úlohy", "úloh")} z rutín na dnes");

        if (payload.Blocks <= 0 && sections.Count == 0)
            return (title, fallback);

        // Only ever sent next to something else, so "no plan yet" reads as a gentle prompt, not an empty report.
        sections.Insert(0, payload.Blocks > 0 ? Plan(payload.Blocks, payload.FirstBlockAt, en) : en ? "No plan yet" : "Zatiaľ bez plánu");
        return (title, string.Join(" · ", sections));
    }

    private static string Plan(int blocks, TimeOnly? firstAt, bool en)
    {
        var plan = en
            ? $"{blocks} {EnNotificationTextKit.Plural(blocks, "block", "blocks")} planned"
            : $"{blocks} {SkNotificationTextKit.Plural(blocks, "naplánovaný blok", "naplánované bloky", "naplánovaných blokov")}";
        return firstAt is { } at ? $"{plan}, {(en ? "first at" : "prvý o")} {at:HH\\:mm}" : plan;
    }
}
