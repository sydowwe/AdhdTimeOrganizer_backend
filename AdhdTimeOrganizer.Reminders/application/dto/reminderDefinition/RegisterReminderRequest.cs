using System.Text.Json.Nodes;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.reminders;

namespace AdhdTimeOrganizer.Reminders.application.dto.reminderDefinition;

/// <summary>
/// Flat request for the diagnostic / manual-ops register endpoint — rebuilt into a
/// <see cref="ReminderRegistration"/> (the typed <see cref="ReminderSchedule"/> is reconstructed from
/// <see cref="ReminderScheduleType"/> + the matching per-type fields). Idempotency, schedule/recipient/text-source
/// validation, and the next-occurrence computation all happen in <see cref="IReminderRegistry"/>; the endpoint
/// only checks per-type required fields the flat→typed mapping can't express.
/// </summary>
public record RegisterReminderRequest
{
    // --- Idempotency key. ---
    public required string OwnerModule { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public required string Kind { get; init; }

    // --- Content. ---
    public required string TemplateKey { get; init; }

    /// <summary>
    /// Free-form payload document. Unlike module code — which registers with a typed
    /// <see cref="IReminderPayload"/> record — this arrives over the wire, so the payload PII contract is
    /// enforced here at runtime: <c>RegisterReminderEndpoint</c> rejects a document carrying a person-data key
    /// with a 400. See <see cref="RawReminderPayload"/>.
    /// </summary>
    public JsonNode? Payload { get; init; }

    public NotificationType? NotificationType { get; init; }

    // --- Schedule (supply the fields for the chosen ReminderScheduleType). ---
    public required ReminderScheduleType ScheduleType { get; init; }
    public DateTimeOffset? DueAt { get; init; }
    public List<int>? LeadOffsetsMinutes { get; init; }
    public ReminderIntervalPreset? IntervalPreset { get; init; }
    public DateTimeOffset? AnchorDate { get; init; }
    public string? Cron { get; init; }
    public DateTimeOffset? EndsAt { get; init; }

    // --- Recipients. ---
    public required RecipientMode RecipientMode { get; init; }
    public List<long>? ExplicitRecipientUserIds { get; init; }
    public string? RecipientResolverKey { get; init; }

    // --- Dispatch-policy hints (phase 04). ---
    public string? DigestKey { get; init; }
    public NotificationChannel? ChannelHint { get; init; }

    /// <summary>
    /// Builds the typed registration. The endpoint rejects a missing per-type field before this runs, so the
    /// schedule factory always gets the fields its <see cref="ScheduleType"/> needs; the registry re-validates.
    /// </summary>
    public ReminderRegistration ToRegistration()
    {
        return new ReminderRegistration
        {
            Key = new ReminderKey(OwnerModule, SubjectType, SubjectId, Kind),
            Schedule = ScheduleType switch
            {
                ReminderScheduleType.OneShot => ReminderSchedule.OneShot(DueAt ?? default, LeadOffsetsMinutes ?? []),
                ReminderScheduleType.RecurringInterval => ReminderSchedule.RecurringInterval(IntervalPreset ?? ReminderIntervalPreset.Daily, AnchorDate ?? default, EndsAt),
                ReminderScheduleType.RecurringCron => ReminderSchedule.RecurringCron(Cron ?? string.Empty, EndsAt),
                _ => throw new ArgumentOutOfRangeException(nameof(ScheduleType), ScheduleType, "Unmapped reminder schedule type.")
            },
            TemplateKey = TemplateKey,
            Payload = Payload is null ? null : new RawReminderPayload(Payload),
            NotificationType = NotificationType,
            RecipientMode = RecipientMode,
            ExplicitRecipientUserIds = ExplicitRecipientUserIds,
            RecipientResolverKey = RecipientResolverKey,
            DigestKey = DigestKey,
            ChannelHint = ChannelHint
        };
    }
}