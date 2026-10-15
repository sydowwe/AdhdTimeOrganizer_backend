using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.action;
using Sydowwe.Framework.Contracts.notification.kind;
using Sydowwe.Framework.Contracts.notification.text;
using Sydowwe.Framework.domain.@enum;

namespace AdhdTimeOrganizer.Planning.application.notification;

/// <summary>
/// A reminder the user set for themselves — a portal <c>Reminder</c> row, standalone or attached to a planner task,
/// delivered through the Reminders module. The user wrote the title, so it is the whole body; it can be absent (an
/// empty or older document), hence the name-less fallback.
/// <para>
/// Not held while away (the user asked for it) and not mailed by default: a self-set nudge the user already sees
/// in-app must not also mail them every time — mailing every "call the dentist at 15:00" would make the inbox the
/// noisy surface the e-mail default exists to protect. Distinct from the Reminders module's generic
/// <c>DeadlineApproaching</c> for exactly that reason.
/// </para>
/// </summary>
public sealed class PersonalReminderNotificationKind : NotificationKind<PersonalReminderPayload>, ISingletonService
{
    public override NotificationType Type => PlanningNotificationTypes.PersonalReminder;

    /// <summary>P2-03: snooze it from the lock screen (Reminders' own handler), or put it away.</summary>
    public override IReadOnlyList<NotificationAction> Actions { get; } = [NotificationAction.Snooze(10), NotificationAction.Dismiss];

    protected override (string Title, string Body) Render(PersonalReminderPayload? payload, NotificationRenderContext context)
    {
        var en = context.Kit.Language == AvailableLocales.En;
        return (en ? "Reminder" : "Pripomienka", NonEmpty(payload?.Title) ?? (en ? "You have a reminder." : "Máte pripomienku."));
    }

    /// <summary>
    /// A reminder attached to a task deep-links to the task: that is the thing the user acts on, and it is what
    /// <c>PlannerTaskId</c> was added for. A standalone reminder — no task to open — falls back to the reminder row.
    /// </summary>
    protected override NotificationSubject? Subject(PersonalReminderPayload? payload) => payload switch
    {
        { PlannerTaskId: { } taskId } => new NotificationSubject(NotificationSubjectKinds.PlannerTask, taskId),
        { ReminderId: var reminderId } => new NotificationSubject(NotificationSubjectKinds.Reminder, reminderId),
        null => null
    };
}

/// <summary>
/// A planned block is about to start — the user's lead time before it, or at the start itself. Raised by a reminder the
/// planner registered <b>automatically</b> when the block was created, under the user's own rule (no blocks, the
/// important ones, or all of them). Its own kind so it gets its own settings row: muting the automatic reminders leaves
/// the ones the user wrote alone. The activity's name is the body, the title says how soon, and each half degrades on
/// its own.
/// </summary>
public sealed class PlannedBlockStartingNotificationKind : NotificationKind<PlannedBlockStartingPayload>, ISingletonService
{
    public override NotificationType Type => PlanningNotificationTypes.PlannedBlockStarting;

    /// <summary>
    /// System-generated and about a moment, so unlike a personal reminder — which the user wrote — it waits in the bell
    /// for a user who is away: a whole day of them is exactly what a returning user should not find on their lock screen.
    /// </summary>
    public override bool HeldWhileAway => true;

    /// <summary>P2-03: start the block, or skip it, from the lock screen (<c>PlannerTaskNotificationActionHandler</c>).</summary>
    public override IReadOnlyList<NotificationAction> Actions { get; } = [NotificationAction.Start, NotificationAction.Skip];

    protected override (string Title, string Body) Render(PlannedBlockStartingPayload? payload, NotificationRenderContext context)
    {
        var en = context.Kit.Language == AvailableLocales.En;
        // "Začína o 10 min", "… o 1 h 30 min"; "Začína teraz"; no lead: "Čoskoro začína".
        var title = payload?.MinutesBefore switch
        {
            null => en ? "Starting soon" : "Čoskoro začína",
            <= 0 => en ? "Starting now" : "Začína teraz",
            var m => en ? $"Starts in {NotificationTextKit.Duration(m.Value)}" : $"Začína o {NotificationTextKit.Duration(m.Value)}"
        };
        return (title, NonEmpty(payload?.Title)
                       ?? (en ? "Your next planned block is about to start." : "Váš ďalší naplánovaný blok čoskoro začína."));
    }

    protected override NotificationSubject? Subject(PlannedBlockStartingPayload? payload) => PlannerTaskSubject.Of(payload?.PlannerTaskId);
}

/// <summary>
/// One or more of today's planned blocks came and went without being started — "2 blocks slipped, re-plan the rest of
/// today?". Composed by a sweep, one notification per user per sweep however many blocks it found, and capped per day so
/// a bad day does not turn into a stream of reproaches. A nudge, not a reproach: it says what happened and offers the
/// next step.
/// </summary>
public sealed class PlannedBlocksMissedNotificationKind : NotificationKind<PlannedBlocksMissedPayload>, ISingletonService
{
    public override NotificationType Type => PlanningNotificationTypes.PlannedBlocksMissed;

    /// <summary>Unsolicited and about a day that passes without it (audit N5).</summary>
    public override bool HeldWhileAway => true;

    protected override (string Title, string Body) Render(PlannedBlocksMissedPayload? payload, NotificationRenderContext context)
    {
        var count = payload?.Count ?? 0;
        return context.Kit.Language == AvailableLocales.En
            ? ("Your plan slipped", count > 0
                ? $"{count} planned {EnNotificationTextKit.Plural(count, "block", "blocks")} slipped by — re-plan the rest of today?"
                : "Some planned blocks slipped by — re-plan the rest of today?")
            // Noun, adjective and verb all agree with the count: "1 naplánovaný blok ušiel", "2 naplánované bloky ušli",
            // "5 naplánovaných blokov ušlo".
            : ("Plán sa posunul", count > 0
                ? $"{count} {SkNotificationTextKit.Plural(count, "naplánovaný blok ušiel", "naplánované bloky ušli", "naplánovaných blokov ušlo")} — preplánovať zvyšok dňa?"
                : "Niektoré naplánované bloky ušli — preplánovať zvyšok dňa?");
    }

    /// <summary>The earliest block that slipped stands for the day: opening it puts the user on that day's plan.</summary>
    protected override NotificationSubject? Subject(PlannedBlocksMissedPayload? payload) => PlannerTaskSubject.Of(payload?.FirstTaskId);
}

/// <summary>
/// A planned block the user started is still running past its planned end — "time's up, wrap up or extend?". Composed
/// by a sweep, once per block end (an extended block can overrun again). Distinct from the History slice's timer
/// boundary, which the user scheduled themselves: this one is the plan's boundary, not the timer's. One sentence covers
/// every degraded shape — it takes a missing name and 0 minutes — so an empty document still reads as an overrun.
/// </summary>
public sealed class PlannedBlockOverrunNotificationKind : NotificationKind<PlannedBlockOverrunPayload>, ISingletonService
{
    public override NotificationType Type => PlanningNotificationTypes.PlannedBlockOverrun;

    /// <summary>About a moment that passes; a returning user does not need a pile of them on the lock screen.</summary>
    public override bool HeldWhileAway => true;

    /// <summary>P2-03: finish the block now, or give it 15 more minutes (<c>PlannerTaskNotificationActionHandler</c>).</summary>
    public override IReadOnlyList<NotificationAction> Actions { get; } = [NotificationAction.Done, NotificationAction.Extend(15)];

    protected override (string Title, string Body) Render(PlannedBlockOverrunPayload? payload, NotificationRenderContext context)
    {
        var kit = context.Kit;
        var activity = NonEmpty(payload?.ActivityName);
        var minutesOver = Math.Max(0, payload?.MinutesOver ?? 0);

        if (kit.Language == AvailableLocales.En)
        {
            // "Time for “Deep work” ran out 10 min ago — wrap up or extend?"; no name: "Your planned block ended …".
            var ago = minutesOver > 0 ? $" {NotificationTextKit.Duration(minutesOver)} ago" : string.Empty;
            return ("Time's up", activity is null
                ? $"Your planned block ended{ago} — wrap up or extend?"
                : $"Time for {kit.Quote(activity)} ran out{ago} — wrap up or extend?");
        }
        else
        {
            // "Čas na „Deep work“ vypršal pred 10 min — ukončiť, alebo predĺžiť?"; no name: "Váš naplánovaný blok skončil …".
            var ago = minutesOver > 0 ? $" pred {NotificationTextKit.Duration(minutesOver)}" : string.Empty;
            return ("Čas vypršal", activity is null
                ? $"Váš naplánovaný blok skončil{ago} — ukončiť, alebo predĺžiť?"
                : $"Čas na {kit.Quote(activity)} vypršal{ago} — ukončiť, alebo predĺžiť?");
        }
    }

    protected override NotificationSubject? Subject(PlannedBlockOverrunPayload? payload) => PlannerTaskSubject.Of(payload?.PlannerTaskId);
}

/// <summary>The planner task a notification links to; none for a missing or non-positive id.</summary>
internal static class PlannerTaskSubject
{
    public static NotificationSubject? Of(long? taskId) =>
        taskId is long id and > 0 ? new NotificationSubject(NotificationSubjectKinds.PlannerTask, id) : null;
}
