using AdhdTimeOrganizer.Reminders.domain.entity;
using AdhdTimeOrganizer.Reminders.domain.@enum;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;
using Sydowwe.Framework.Contracts.notification;

namespace AdhdTimeOrganizer.Reminders.application.dto.reminderDispatch;

/// <summary>
/// A single row from the append-only dispatch ledger — the snapshot fields plus the <see cref="Outcome"/>.
/// Backs the dispatch history / audit view (phase 05). SQL-translatable, read-only (<c>AsNoTracking</c>).
/// </summary>
public record ReminderDispatchDto : IdResponse, IProjectionResponse<ReminderDispatchDto, ReminderDispatch>
{
    public required long ReminderDefinitionId { get; init; }

    public required DateTimeOffset OccurrenceAt { get; init; }
    public required DateTimeOffset DispatchedAt { get; init; }

    public required DispatchOutcome Outcome { get; init; }
    public SkipReason? SkipReason { get; init; }

    public NotificationType? NotificationTypeSnapshot { get; init; }
    public string? TemplateKeySnapshot { get; init; }
    public required string RecipientsSnapshot { get; init; }

    public required string CorrelationId { get; init; }
    public long? ReversesDispatchId { get; init; }

    public static IQueryable<ReminderDispatchDto> Projection(IQueryable<ReminderDispatch> query)
    {
        return query.Select(x => new ReminderDispatchDto
        {
            Id = x.Id,
            ReminderDefinitionId = x.ReminderDefinitionId,
            OccurrenceAt = x.OccurrenceAt,
            DispatchedAt = x.DispatchedAt,
            Outcome = x.Outcome,
            SkipReason = x.SkipReason,
            NotificationTypeSnapshot = x.NotificationTypeSnapshot,
            TemplateKeySnapshot = x.TemplateKeySnapshot,
            RecipientsSnapshot = x.RecipientsSnapshot,
            CorrelationId = x.CorrelationId,
            ReversesDispatchId = x.ReversesDispatchId
        });
    }
}