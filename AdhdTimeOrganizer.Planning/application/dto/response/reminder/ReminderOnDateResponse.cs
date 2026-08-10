using AdhdTimeOrganizer.Core.domain.model.@enum;
using Sydowwe.Framework.application.dto.response.@base;

namespace AdhdTimeOrganizer.Planning.application.dto.response.reminder;

/// <summary>
/// A reminder as it appears on one particular day. Distinct from <see cref="ReminderResponse"/> because of
/// <see cref="OccursAt"/>: for a repeating reminder, <see cref="RemindAt"/> is the anchor it repeats from
/// (a birthday's anchor may be years in the past) while <see cref="OccursAt"/> is the instant it lands on
/// the requested day. For a one-shot reminder the two are the same.
/// </summary>
public record ReminderOnDateResponse : IIdResponse
{
    public required long Id { get; init; }
    public required string Title { get; init; }
    public string? Note { get; init; }

    /// <summary>The stored instant — for a repeating reminder, the recurrence anchor, not this day's fire.</summary>
    public required DateTime RemindAt { get; init; }

    /// <summary>When this reminder lands on the requested day (UTC).</summary>
    public required DateTime OccursAt { get; init; }

    public required List<int> LeadOffsetsMinutes { get; init; }
    public ReminderRecurrence? Recurrence { get; init; }
    public long? PlannerTaskId { get; init; }
}