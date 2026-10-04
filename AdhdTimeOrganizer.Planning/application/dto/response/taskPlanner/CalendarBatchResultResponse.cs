namespace AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;

/// <summary>
/// The answer shape shared by every per-day batch write on the calendar — template apply, day copy and
/// day-type change. One entry per requested calendar id, in request order and of the same length, so the
/// caller can render "N of M succeeded" from one component regardless of which batch it fired.
/// <para>
/// <b>Deliberately not transactional.</b> These replace client-side fan-outs of the per-day endpoints, which
/// were never atomic either: each day is applied and committed on its own, and a day that fails leaves the
/// days around it applied. That is what makes partial success expressible at all — a single transaction could
/// only ever answer all-or-nothing.
/// </para>
/// </summary>
public record CalendarBatchResultResponse
{
    public required List<CalendarBatchResult> Results { get; init; }

    /// <summary>Convenience for the caller's snackbar; derived, never sent by a client.</summary>
    public int SucceededCount => Results.Count(r => r.Succeeded);
}

public record CalendarBatchResult
{
    public required long CalendarId { get; init; }

    public required bool Succeeded { get; init; }

    /// <summary>
    /// Why this day failed; <c>null</c> when it succeeded. Lets the client name the day <i>and</i> the cause —
    /// "Workday on a Saturday" is something the user can act on, a bare <c>succeeded: false</c> is not.
    /// </summary>
    public CalendarBatchFailureReason? FailureReason { get; init; }

    public static CalendarBatchResult Ok(long calendarId) => new() { CalendarId = calendarId, Succeeded = true };

    public static CalendarBatchResult Failed(long calendarId, CalendarBatchFailureReason reason) =>
        new() { CalendarId = calendarId, Succeeded = false, FailureReason = reason };
}

public enum CalendarBatchFailureReason
{
    /// <summary>The id resolves to no day the caller owns — stale, deleted, or another user's.</summary>
    NotFound,

    /// <summary>The day type is impossible on that weekday (<c>CalendarDayTypeRule</c>). Day-type batch only.</summary>
    DayTypeNotAllowed,

    /// <summary>The day's write threw and was rolled back; the cause is in the server log, not the response.</summary>
    Error
}
