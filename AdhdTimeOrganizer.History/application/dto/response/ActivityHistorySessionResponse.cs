using AdhdTimeOrganizer.History.domain.model.@enum;

namespace AdhdTimeOrganizer.History.application.dto.response.activityHistory;

/// <summary>
/// The session a row belongs to, repeated on every row of it so the timeline can group a day's rows without a
/// second request. Null on the row when it was written outside a session.
/// </summary>
public record ActivityHistorySessionResponse
{
    public required long Id { get; init; }
    public required TimerSessionKind Kind { get; init; }
    public required TimerSessionOutcome Outcome { get; init; }
    public int? PlannedFocusPeriods { get; init; }
    public int? CompletedFocusPeriods { get; init; }
}

/// <summary>The answer to <c>POST /activity-history/session</c>.</summary>
public record CreateActivityHistorySessionResponse
{
    public required long SessionId { get; init; }

    /// <summary>In the order the records were sent.</summary>
    public required List<long> RecordIds { get; init; }
}
