using MojaDigitalnaFirma.Kernel.reminders;

namespace AdhdTimeOrganizer.Reminders.application.dto.reminderDefinition;

/// <summary>Identifies a single registered reminder by its <see cref="ReminderKey"/> 4-tuple for cancel / pause / resume.</summary>
public record ReminderKeyRequest
{
    public required string OwnerModule { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public required string Kind { get; init; }

    public ReminderKey ToKey() => new(OwnerModule, SubjectType, SubjectId, Kind);
}