using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.History.domain.model.@enum;
using Sydowwe.Framework.domain.valueObject;

namespace AdhdTimeOrganizer.History.application.dto.request.history;

/// <summary>
/// One finished Pomodoro or countdown, posted once on save: the session's outcome plus one record per phase that
/// actually ran, each at its real start. Written all-or-nothing.
/// </summary>
/// <remarks>
/// A timer embedded in a planner task or to-do item posts here too (frontend OP6), and says which item through
/// <see cref="TodoListItemId"/> / <see cref="RoutineTodoListId"/> — the same two links, with the same rules, as
/// <see cref="ActivityHistoryRequest"/>. They are stamped on the session's <b>work</b> rows only; see
/// <see cref="IsWorkRecord"/>.
/// </remarks>
public record CreateActivityHistorySessionRequest
{
    public required TimerSessionKind Kind { get; init; }

    public required TimerSessionOutcome Outcome { get; init; }

    /// <summary>Non-null for a Pomodoro, null for a Timer.</summary>
    public int? PlannedFocusPeriods { get; init; }

    /// <summary>Non-null for a Pomodoro, null for a Timer; never more than <see cref="PlannedFocusPeriods"/>.</summary>
    public int? CompletedFocusPeriods { get; init; }

    /// <summary>
    /// The focus time the timer was set for — a countdown's length, or a Pomodoro's focus length × planned focus
    /// periods. Optional; positive when sent.
    /// </summary>
    public IntTime? PlannedLength { get; init; }

    /// <summary>
    /// The to-do item the timer was run for, when it was started from one. This is what puts the session's time
    /// on that item's row in the daily recap (<c>TodoListItemLoggedTimeSource</c>). Null for a timer run on its own.
    /// </summary>
    public long? TodoListItemId { get; init; }

    /// <summary>Same as <see cref="TodoListItemId"/>, for a routine item. Never both.</summary>
    public long? RoutineTodoListId { get; init; }

    /// <summary>
    /// The session is one sitting of the linked item, so the server ticks the item once — see
    /// <see cref="ActivityHistoryRequest.CountsAsRepetition"/>. Requires a link. Not stored.
    /// </summary>
    public bool CountsAsRepetition { get; init; }

    /// <summary>In start order. At least one; exactly one for a Timer.</summary>
    public required List<ActivityHistorySessionRecordRequest> Records { get; init; }

    public ActivityHistorySession ToEntity(long userId) => new()
    {
        UserId = userId,
        Kind = Kind,
        Outcome = Outcome,
        PlannedFocusPeriods = PlannedFocusPeriods,
        CompletedFocusPeriods = CompletedFocusPeriods,
        PlannedLength = PlannedLength,
        Records = Records.Select(r => ToRecordEntity(r, userId)).ToList()
    };

    /// <summary>
    /// A countdown's one row, or a Pomodoro's focus row. A break is logged against the rest activity and is not the
    /// item's time, so it never carries the link — the recap would otherwise credit the item with its rests.
    /// </summary>
    private static bool IsWorkRecord(ActivityHistorySessionRecordRequest record) =>
        record.Phase is null or TimerSessionPhase.Focus;

    private ActivityHistory ToRecordEntity(ActivityHistorySessionRecordRequest record, long userId)
    {
        var entity = record.ToEntity(userId);
        if (!IsWorkRecord(record)) return entity;
        entity.TodoListItemId = TodoListItemId;
        entity.RoutineTodoListId = RoutineTodoListId;
        return entity;
    }
}

/// <summary>One phase of the session. Every rule of a single <c>POST /activity-history</c> row applies to it.</summary>
public record ActivityHistorySessionRecordRequest
{
    public required long ActivityId { get; init; }

    public required DateTime StartTimestamp { get; init; }

    public required IntTime Length { get; init; }

    /// <summary>Non-null on a Pomodoro record, null on a Timer record.</summary>
    public TimerSessionPhase? Phase { get; init; }

    public ActivityHistory ToEntity(long userId)
    {
        // The validator guarantees an offset; a non-Z one arrives as Kind=Local, which Npgsql refuses for a
        // timestamptz column. Normalised here so every record is stored as the instant it names.
        var start = StartTimestamp.ToUniversalTime();

        return new ActivityHistory
        {
            UserId = userId,
            ActivityId = ActivityId,
            StartTimestamp = start,
            Length = Length,
            EndTimestamp = start.AddSeconds(Length.TotalSeconds),
            Phase = Phase
        };
    }
}
