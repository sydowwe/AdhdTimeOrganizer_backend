using AdhdTimeOrganizer.Reminders.domain.@enum;
using Sydowwe.Framework.application.dto.request.@interface;
using Sydowwe.Framework.Contracts.reminders;

namespace AdhdTimeOrganizer.Reminders.application.dto.dashboard;

/// <summary>
/// Filter for the upcoming-reminders dashboard (phase 05a): by owner module, subject type, kind, recipient,
/// schedule type, status, and next-occurrence range. The endpoint already base-filters to definitions that
/// have a pending occurrence (<c>NextOccurrenceAt != null</c>), so <see cref="Status"/> here practically only
/// ever matches <see cref="ReminderStatus.Active"/> (pause / cancel / complete all clear the next occurrence).
/// </summary>
public record UpcomingReminderFilterRequest : IFilterRequest
{
    public string? OwnerModule { get; set; }
    public string? SubjectType { get; set; }
    public string? Kind { get; set; }

    /// <summary>Narrow to definitions that have this explicit recipient (<c>ExplicitUsers</c> mode only).</summary>
    public long? RecipientUserId { get; set; }

    public ReminderStatus? Status { get; set; }
    public ReminderScheduleType? ScheduleType { get; set; }

    public DateTimeOffset? NextOccurrenceFrom { get; set; }
    public DateTimeOffset? NextOccurrenceTo { get; set; }
}