using AdhdTimeOrganizer.Reminders.domain.entity;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;
using Sydowwe.Framework.Contracts.reminders;

namespace AdhdTimeOrganizer.Reminders.application.dto.dashboard;

/// <summary>
/// Slim self-service row for the recipient's own upcoming reminders (phase 05a,
/// <c>GetMyUpcomingRemindersEndpoint</c>). Deliberately omits the owner-supplied <c>PayloadJson</c>, the
/// co-recipient list, and internal routing metadata (resolver/digest/channel hints) that the full
/// <see cref="reminderDefinition.ReminderDefinitionDto"/> carries — those stay on the Admin inspector +
/// admin upcoming dashboard only, never crossing to a non-admin recipient. SQL-translatable, read-only.
/// </summary>
public record MyUpcomingReminderDto : IdResponse, IProjectionResponse<MyUpcomingReminderDto, ReminderDefinition>
{
    public required string OwnerModule { get; init; }
    public required string Kind { get; init; }
    public required string TemplateKey { get; init; }
    public required ReminderScheduleType ScheduleType { get; init; }
    public DateTimeOffset? NextOccurrenceAt { get; init; }

    public static IQueryable<MyUpcomingReminderDto> Projection(IQueryable<ReminderDefinition> query)
    {
        return query.Select(x => new MyUpcomingReminderDto
        {
            Id = x.Id,
            OwnerModule = x.OwnerModule,
            Kind = x.Kind,
            TemplateKey = x.TemplateKey,
            ScheduleType = x.ScheduleType,
            NextOccurrenceAt = x.NextOccurrenceAt
        });
    }
}