using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.History.domain.model.@enum;
using Sydowwe.Framework.domain.valueObject;

namespace AdhdTimeOrganizer.History.application.dto.request.history;

/// <summary>
/// One finished Pomodoro or countdown, posted once on save: the session's outcome plus one record per phase that
/// actually ran, each at its real start. Written all-or-nothing.
/// </summary>
/// <remarks>
/// Carries no to-do link, on purpose. When a timer is embedded in a planner task or to-do item it emits one total
/// and the embedder writes one linked row through <c>POST /activity-history</c>; that path does not become a
/// session.
/// </remarks>
public record CreateActivityHistorySessionRequest
{
    public required TimerSessionKind Kind { get; init; }

    public required TimerSessionOutcome Outcome { get; init; }

    /// <summary>Non-null for a Pomodoro, null for a Timer.</summary>
    public int? PlannedFocusPeriods { get; init; }

    /// <summary>Non-null for a Pomodoro, null for a Timer; never more than <see cref="PlannedFocusPeriods"/>.</summary>
    public int? CompletedFocusPeriods { get; init; }

    /// <summary>In start order. At least one; exactly one for a Timer.</summary>
    public required List<ActivityHistorySessionRecordRequest> Records { get; init; }

    public ActivityHistorySession ToEntity(long userId) => new()
    {
        UserId = userId,
        Kind = Kind,
        Outcome = Outcome,
        PlannedFocusPeriods = PlannedFocusPeriods,
        CompletedFocusPeriods = CompletedFocusPeriods,
        Records = Records.Select(r => r.ToEntity(userId)).ToList()
    };
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
