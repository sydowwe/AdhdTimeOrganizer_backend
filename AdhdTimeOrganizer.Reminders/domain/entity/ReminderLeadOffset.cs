using Sydowwe.Framework.domain.entity.@base;

namespace AdhdTimeOrganizer.Reminders.domain.entity;

/// <summary>
/// A lead-time offset on a one-shot <see cref="ReminderDefinition"/> deadline. Each offset produces its own
/// occurrence instant relative to <see cref="ReminderDefinition.DueAt"/>: <see cref="OffsetMinutes"/> is
/// minutes relative to the deadline — negative = before it (e.g. 30 days before = <c>-43200</c>),
/// <c>0</c> = at the deadline — matching the contract's <c>ReminderSchedule.LeadOffsetsMinutes</c>.
/// </summary>
public class ReminderLeadOffset : BaseTableEntity
{
    public long ReminderDefinitionId { get; set; }
    public ReminderDefinition ReminderDefinition { get; set; } = null!;

    public int OffsetMinutes { get; set; }
}