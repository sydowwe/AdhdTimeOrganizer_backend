using AdhdTimeOrganizer.Planning.domain.model.@enum;
using Sydowwe.Framework.application.dto.dto;

namespace AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;

/// <summary>
/// <c>POST /calendar/day-plan/{date}/replan</c> — what re-planning the rest of today would do (FO4). Nothing is
/// written; <c>POST …/replan/apply</c> with <see cref="PreviewToken"/> does it.
/// <para>
/// <b>Nothing slipped is a 200</b> with empty <see cref="Moves"/> and <see cref="WontFit"/> (and
/// <see cref="HasChanges"/> false), never an error.
/// </para>
/// </summary>
public record DayReplanPreviewResponse
{
    /// <summary>The date planned, as parsed — always the user's today.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>Opaque. Echo it back to the apply unchanged; it is good for ten minutes and for this day as it stands.</summary>
    public required string PreviewToken { get; init; }

    /// <summary>The start of the window: the user's now, rounded up to the whole minute.</summary>
    public required TimeDto PackedFrom { get; init; }

    /// <summary>The end of the window: bed time, or 23:59 when bed time is 00:00 or after midnight.</summary>
    public required TimeDto WindowEnd { get; init; }

    /// <summary>The blocks whose slot changes, ordered by their new start. A block that keeps its slot is absent.</summary>
    public required IReadOnlyList<DayReplanMoveResponse> Moves { get; init; }

    /// <summary>The blocks with no room left today, optional ones first, then in the day's order.</summary>
    public required IReadOnlyList<DayReplanWontFitResponse> WontFit { get; init; }

    /// <summary>Whether applying would change anything.</summary>
    public bool HasChanges => Moves.Count > 0 || WontFit.Count > 0;
}

/// <summary>One block the re-plan moves within the day.</summary>
public record DayReplanMoveResponse
{
    /// <summary>The task as it stands now — its current <c>startTime</c> / <c>endTime</c> are where it moves from.</summary>
    public required PlannerTaskResponse Task { get; init; }

    /// <summary><c>"Missed"</c>, <c>"Late"</c> or <c>"Upcoming"</c> (pushed later to make room).</summary>
    public required PlannerTaskSlipState State { get; init; }

    /// <summary>The proposed new start. The length is kept.</summary>
    public required TimeDto StartTime { get; init; }

    /// <summary>The proposed new end.</summary>
    public required TimeDto EndTime { get; init; }
}

/// <summary>One block the re-plan has no room for today; the apply must resolve it (tomorrow or skip).</summary>
public record DayReplanWontFitResponse
{
    public required PlannerTaskResponse Task { get; init; }

    /// <summary><c>"Missed"</c>, <c>"Late"</c> or <c>"Upcoming"</c>.</summary>
    public required PlannerTaskSlipState State { get; init; }

    /// <summary>Whether its importance is the "optional" level — those give way first.</summary>
    public required bool IsOptional { get; init; }
}
