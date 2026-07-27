using AdhdTimeOrganizer.Reminders.domain.@enum;
using MojaDigitalnaFirma.Kernel.reminders;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.Reminders.application.dto.reminderDefinition;

/// <summary>
/// Filter for the registered-reminders grid: by owner module, subject type, kind, status, schedule type, and
/// next-occurrence range. Mirrors Scheduler's <c>ScheduledJobFilterRequest</c>.
/// </summary>
public record ReminderDefinitionFilterRequest : IFilterRequest
{
    public string? OwnerModule { get; set; }
    public string? SubjectType { get; set; }
    public string? Kind { get; set; }
    public ReminderStatus? Status { get; set; }
    public ReminderScheduleType? ScheduleType { get; set; }
    public DateTimeOffset? NextOccurrenceFrom { get; set; }
    public DateTimeOffset? NextOccurrenceTo { get; set; }
}