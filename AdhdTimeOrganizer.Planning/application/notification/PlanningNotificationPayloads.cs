using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Contracts.reminders;

namespace AdhdTimeOrganizer.Planning.application.notification;

// Read INotificationPayload first: ids and non-person scalars only. Property names are the persisted camelCase JSON
// keys, so renaming one is a storage change — an old row keeps the old key and its kind renders the fallback.

/// <summary>
/// A reminder the user set for themselves — a portal <c>Reminder</c> row, standalone or attached to a
/// <c>PlannerTask</c>. <c>Title</c> is the user's own text for their own nudge — free text, but it is <b>their</b>
/// text about <b>their</b> reminder, and the row it is copied from is deleted with their account. Nothing
/// person-shaped about anyone else belongs here. <c>PlannerTaskId</c> is present so the client can deep-link back to
/// the task; a standalone reminder leaves it null.
/// <para>
/// Implements <see cref="IReminderPayload"/> as well because it is written at <i>registration</i> time (persisted to
/// <c>ReminderDefinition.PayloadJson</c>) and read back at <i>dispatch</i> time by the kind — both markers apply to
/// the same document, and both are walked by the same PII guard.
/// </para>
/// </summary>
[NotificationPayload(PlanningNotificationTypes.PersonalReminderKey)]
public sealed record PersonalReminderPayload(
    long ReminderId,
    string? Title = null,
    long? PlannerTaskId = null) : INotificationPayload, IReminderPayload;

/// <summary>
/// A planned block is about to start, from a reminder the planner registered for it automatically. Registered through
/// the Reminders module as a one-shot at the block's start with the user's lead, so it is frozen at registration: the
/// owner re-registers on every move and guards the dispatch (<c>IReminderDispatchGuard</c>).
/// <para>
/// <c>Title</c> is the name of the user's own activity the block is for, and its one recipient is that same user — the
/// <see cref="PersonalReminderPayload"/> precedent. <c>StartsAt</c> is the start instant the registration was built
/// from (UTC); the guard compares it with the block's current one, so a stale registration retires itself.
/// <c>MinutesBefore</c> is the lead the occurrence fires at, 0 for "starting now", so the title can say which.
/// <c>ReminderId</c> is the portal reminder row the registration is keyed on.
/// </para>
/// </summary>
[NotificationPayload(PlanningNotificationTypes.PlannedBlockStartingKey)]
public sealed record PlannedBlockStartingPayload(
    long PlannerTaskId,
    long? ReminderId = null,
    string? Title = null,
    DateTimeOffset? StartsAt = null,
    int? MinutesBefore = null) : INotificationPayload, IReminderPayload;

/// <summary>
/// Today's planned blocks that slipped by unstarted, found by one sweep. Ids and counts only. <c>Date</c> is the user's
/// own day the blocks were planned on — what a re-plan of "the rest of today" acts on. <c>Count</c> is how many blocks
/// this sweep newly found, not the day's total. <c>FirstTaskId</c> is the earliest of them, so the notification has a
/// subject to link to.
/// </summary>
[NotificationPayload(PlanningNotificationTypes.PlannedBlocksMissedKey)]
public sealed record PlannedBlocksMissedPayload(
    DateOnly Date,
    int Count,
    long? FirstTaskId = null) : INotificationPayload;

/// <summary>
/// A started planned block running past its planned end. <c>ActivityName</c> is the name of the user's own activity the
/// block is for, and its one recipient is that same user. <c>MinutesOver</c> is how far past the end the sweep found
/// it, 0 when unknown.
/// </summary>
[NotificationPayload(PlanningNotificationTypes.PlannedBlockOverrunKey)]
public sealed record PlannedBlockOverrunPayload(
    long PlannerTaskId,
    string? ActivityName = null,
    int MinutesOver = 0) : INotificationPayload;
