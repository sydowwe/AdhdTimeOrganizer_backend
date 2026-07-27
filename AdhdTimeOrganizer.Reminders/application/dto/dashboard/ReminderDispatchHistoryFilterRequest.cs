using AdhdTimeOrganizer.Reminders.domain.@enum;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.Reminders.application.dto.dashboard;

/// <summary>
/// Filter for the dispatch-history view (phase 05a) over the append-only <c>ReminderDispatch</c> ledger: by
/// reminder, owner module, recipient, outcome, and dispatched-at range.
/// <para>
/// <b>Recipient filter caveat:</b> a dispatch row's actual recipients are the immutable <c>RecipientsSnapshot</c>
/// jsonb (ids only). To stay SQL-translatable, <see cref="RecipientUserId"/> filters by the owning definition's
/// <em>current</em> explicit recipients (<c>ReminderDefinition.Recipients</c>), not the historical snapshot — so
/// a recipient removed after a dispatch won't match its old rows. The snapshot is still returned verbatim per row.
/// </para>
/// </summary>
public record ReminderDispatchHistoryFilterRequest : IFilterRequest
{
    public long? ReminderDefinitionId { get; set; }
    public string? OwnerModule { get; set; }

    /// <summary>See the caveat above — filters by the definition's current explicit recipients, not the snapshot.</summary>
    public long? RecipientUserId { get; set; }

    public DispatchOutcome? Outcome { get; set; }

    public DateTimeOffset? DispatchedFrom { get; set; }
    public DateTimeOffset? DispatchedTo { get; set; }
}