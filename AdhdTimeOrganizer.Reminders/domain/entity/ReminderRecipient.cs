using Sydowwe.Framework.domain.entity.@base;

namespace AdhdTimeOrganizer.Reminders.domain.entity;

/// <summary>
/// An explicit recipient of a <see cref="ReminderDefinition"/> (when its <c>RecipientMode</c> is
/// <c>ExplicitUsers</c>). Modelled as a child row rather than a denormalised array so recipients project
/// and index cleanly.
/// <para>
/// <see cref="UserId"/> is a plain indexed column with <b>no FK to the user table</b>: the orchestration
/// requires recipient user ids to be validated through the user module's query surface at registration
/// (phase 02), not duplicated/coupled here — keeping this module domain-free. The only relationship is the
/// owning <see cref="ReminderDefinitionId"/>.
/// </para>
/// </summary>
public class ReminderRecipient : BaseTableEntity
{
    public long ReminderDefinitionId { get; set; }
    public ReminderDefinition ReminderDefinition { get; set; } = null!;

    public long UserId { get; set; }
}