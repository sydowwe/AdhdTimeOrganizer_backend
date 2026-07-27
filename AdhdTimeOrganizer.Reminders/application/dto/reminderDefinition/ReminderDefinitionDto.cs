using AdhdTimeOrganizer.Reminders.domain.entity;
using AdhdTimeOrganizer.Reminders.domain.@enum;
using MojaDigitalnaFirma.Kernel.notification;
using MojaDigitalnaFirma.Kernel.reminders;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;

namespace AdhdTimeOrganizer.Reminders.application.dto.reminderDefinition;

/// <summary>
/// Registry overview / inspector row for a registered reminder — one per <c>ReminderDefinition</c>, with its
/// explicit recipients and one-shot lead offsets projected as children. Backs the registry inspector (phase
/// 02b) and the upcoming-reminders dashboard (phase 05). SQL-translatable, read-only (<c>AsNoTracking</c>).
/// </summary>
public record ReminderDefinitionDto : IdResponse, IProjectionResponse<ReminderDefinitionDto, ReminderDefinition>
{
    // --- Idempotency key. ---
    public required string OwnerModule { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public required string Kind { get; init; }

    // --- Content. ---
    public required string TemplateKey { get; init; }
    public required string PayloadJson { get; init; }
    public NotificationType? NotificationType { get; init; }

    // --- Schedule (per-type fields meaningful only for the matching ScheduleType). ---
    public required ReminderScheduleType ScheduleType { get; init; }
    public DateTimeOffset? DueAt { get; init; }
    public ReminderIntervalPreset? IntervalPreset { get; init; }
    public string? Cron { get; init; }
    public DateTimeOffset? AnchorDate { get; init; }
    public DateTimeOffset? EndsAt { get; init; }

    // --- Recipients. ---
    public required RecipientMode RecipientMode { get; init; }
    public string? RecipientResolverKey { get; init; }
    public required List<ReminderRecipientDto> Recipients { get; init; }
    public required List<ReminderLeadOffsetDto> LeadOffsets { get; init; }

    // --- Scheduler state. ---
    public required ReminderStatus Status { get; init; }
    public DateTimeOffset? NextOccurrenceAt { get; init; }
    public DateTimeOffset? LastOccurrenceAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }

    // --- Dispatch-policy hints. ---
    public string? DigestKey { get; init; }
    public NotificationChannel? ChannelHint { get; init; }

    public required bool IsActive { get; init; }

    public static IQueryable<ReminderDefinitionDto> Projection(IQueryable<ReminderDefinition> query)
    {
        return query.Select(x => new ReminderDefinitionDto
        {
            Id = x.Id,
            OwnerModule = x.OwnerModule,
            SubjectType = x.SubjectType,
            SubjectId = x.SubjectId,
            Kind = x.Kind,
            TemplateKey = x.TemplateKey,
            PayloadJson = x.PayloadJson,
            NotificationType = x.NotificationType,
            ScheduleType = x.ScheduleType,
            DueAt = x.DueAt,
            IntervalPreset = x.IntervalPreset,
            Cron = x.Cron,
            AnchorDate = x.AnchorDate,
            EndsAt = x.EndsAt,
            RecipientMode = x.RecipientMode,
            RecipientResolverKey = x.RecipientResolverKey,
            Recipients = x.Recipients
                .OrderBy(r => r.UserId)
                .Select(r => new ReminderRecipientDto
                {
                    Id = r.Id,
                    UserId = r.UserId
                })
                .ToList(),
            LeadOffsets = x.LeadOffsets
                .OrderBy(o => o.OffsetMinutes)
                .Select(o => new ReminderLeadOffsetDto
                {
                    Id = o.Id,
                    OffsetMinutes = o.OffsetMinutes
                })
                .ToList(),
            Status = x.Status,
            NextOccurrenceAt = x.NextOccurrenceAt,
            LastOccurrenceAt = x.LastOccurrenceAt,
            CompletedAt = x.CompletedAt,
            DigestKey = x.DigestKey,
            ChannelHint = x.ChannelHint,
            IsActive = x.IsActive
        });
    }
}

/// <summary>An explicit recipient (user id only — no PII) of a <see cref="ReminderDefinitionDto"/>.</summary>
public record ReminderRecipientDto : IdResponse
{
    public required long UserId { get; init; }
}

/// <summary>A one-shot lead-time offset in minutes relative to the deadline (negative = before it).</summary>
public record ReminderLeadOffsetDto : IdResponse
{
    public required int OffsetMinutes { get; init; }
}