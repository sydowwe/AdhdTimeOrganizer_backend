using AdhdTimeOrganizer.Planning.application.dto.@enum;

namespace AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;

/// <summary>
/// One template applied to several days at once. Everything except the day is shared across the batch, which
/// is exactly what the client-side fan-out repeated per request: same template, same conflict resolution,
/// same task list, only the calendar id varying.
/// <para>
/// No <c>Date</c> counterpart to <see cref="CalendarIds"/> on purpose. The single-day endpoint accepts a date
/// so it can lazily create an unplanned day; the bulk-apply action bar selects days that are already on
/// screen, so every id here came from <c>calendar/filter</c> and already exists.
/// </para>
/// </summary>
public record ApplyTemplateToCalendarsBatchRequest
{
    public required long TemplateId { get; init; }

    /// <summary>The days to apply to. Distinct — see <c>ApplyTemplateToCalendarsBatchValidator</c>.</summary>
    public required List<long> CalendarIds { get; init; }

    /// <summary>Applied identically to every day in <see cref="CalendarIds"/>.</summary>
    public required ApplyTemplateConflictResolutionEnum ConflictResolution { get; init; }

    /// <summary>
    /// The template's tasks, same per-task shape as the single-day request. Any <c>CalendarId</c> / <c>Date</c>
    /// on an individual task is ignored: the day comes from <see cref="CalendarIds"/>, and each day gets its
    /// own freshly built copy of this list.
    /// </summary>
    public required List<TemplateAppliedTaskRequest> TasksFromTemplate { get; init; }
}
