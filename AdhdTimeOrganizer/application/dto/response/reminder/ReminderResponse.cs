using AdhdTimeOrganizer.domain.model.entity.reminder;
using AdhdTimeOrganizer.domain.model.@enum;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.reminder;

/// <summary>
/// A personal reminder as the user authored it. Scheduler state (has it fired, when is it next due) is not
/// here on purpose — that lives in the Reminders module and the client reads it from
/// <c>POST /reminder-dashboard/my-upcoming</c>, which is already self-scoped.
/// </summary>
public record ReminderResponse : IIdResponse, IProjectionResponse<ReminderResponse, Reminder>
{
    public required long Id { get; init; }
    public required string Title { get; init; }
    public string? Note { get; init; }
    public required DateTime RemindAt { get; init; }
    public required List<int> LeadOffsetsMinutes { get; init; }
    public ReminderRecurrence? Recurrence { get; init; }
    public long? PlannerTaskId { get; init; }

    public static IQueryable<ReminderResponse> Projection(IQueryable<Reminder> query)
    {
        return query.Select(r => new ReminderResponse
        {
            Id = r.Id,
            Title = r.Title,
            Note = r.Note,
            RemindAt = r.RemindAt,
            LeadOffsetsMinutes = r.LeadOffsetsMinutes,
            Recurrence = r.Recurrence,
            PlannerTaskId = r.PlannerTaskId
        });
    }
}
