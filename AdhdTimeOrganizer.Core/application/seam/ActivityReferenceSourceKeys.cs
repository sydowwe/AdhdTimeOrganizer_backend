namespace AdhdTimeOrganizer.Core.application.seam;

/// <summary>
/// The known <see cref="IActivityReferenceSource.Key"/> values — one per slice that holds an activity
/// FK, plus Core's own timer presets.
/// </summary>
/// <remarks>
/// <para>
/// This list <b>is</b> the answer to "what counts as a reference?" — it is what <c>usageCount</c> sums
/// and what <c>POST /activity/merge</c> repoints. Nothing is deliberately excluded from the count: if
/// a row would break when the activity went away, it is counted, because <c>canDelete</c> is a promise
/// the UI renders as an enabled delete button.
/// </para>
/// <para>
/// Two things the frontend's assumed list named do <b>not</b> appear here, because they do not exist in
/// this solution: <c>alarms</c> (there is no such entity; the Reminders module keys its definitions by
/// string and holds no activity FK) and any separate "routine item" table beyond
/// <c>RoutineTodoList</c>. Two more are read-models rather than references —
/// <c>PlannerSuggestionFromActivityHistory</c> and <c>PlannerSuggestionFromPlannerTask</c> sit over
/// materialized views derived from rows that <em>are</em> counted, so counting them would double-count
/// and repointing them is impossible.
/// </para>
/// <para>
/// A key added here with no implementation shipped fails <c>SeamWiringTests</c>, not production.
/// </para>
/// </remarks>
public static class ActivityReferenceSourceKeys
{
    /// <summary>Core's own <c>TimerPreset.ActivityId</c> plus <c>PomodoroTimerPreset</c>'s two columns.</summary>
    public const string TimerPreset = "timerPreset";

    /// <summary>History's <c>ActivityHistory</c> — the rows the whole feature exists to protect.</summary>
    public const string ActivityHistory = "activityHistory";

    /// <summary><c>TodoListItem.ActivityId</c> and its <c>PairedLeisureActivityId</c> temptation bundle.</summary>
    public const string TodoList = "todoList";

    /// <summary>Routines' <c>RoutineTodoList</c>.</summary>
    public const string RoutineTodoList = "routineTodoList";

    /// <summary>Planning's three concrete task types — one-off, repeating and template.</summary>
    public const string PlannerTask = "plannerTask";

    /// <summary>Tracking's desktop and android pattern→activity mappings.</summary>
    public const string TrackerMapping = "trackerMapping";

    /// <summary>
    /// ActivityProfiles' three 1:1 profiles, <c>MemoryAnchor</c> and <c>LeisureSuggestionRecord</c>.
    /// </summary>
    public const string ActivityProfile = "activityProfile";
}
