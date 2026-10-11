namespace AdhdTimeOrganizer.Planning.domain.model.@enum;

/// <summary>
/// Which newly created planner blocks get a reminder without the user asking for one (audit N1) — the
/// <c>UserPlannerSettings.AutoReminderMode</c> setting, read by <c>PlannedBlockAutoReminders</c>. A mode rather than
/// a bool because a full day is ten to fifteen blocks, and a push for every one of them is the noise that makes
/// users mute everything.
/// <para>
/// Only ever about blocks that are <b>foreground</b>, <c>NotStarted</c> and still ahead when they are created; the
/// mode narrows that further. Stored by name (<c>EnumColumn</c>), so the order here is free to change.
/// </para>
/// </summary>
public enum AutoReminderMode
{
    /// <summary>No block gets one. Reminders the user writes are unaffected.</summary>
    Off,

    /// <summary>
    /// Blocks whose importance is at least <c>UserPlannerSettings.AutoReminderMinImportance</c> on the user's own
    /// scale. A block with no importance, or with the Optional level, never qualifies.
    /// </summary>
    ImportantOnly,

    /// <summary>Every eligible block, Optional ones and ones with no importance included.</summary>
    All
}
