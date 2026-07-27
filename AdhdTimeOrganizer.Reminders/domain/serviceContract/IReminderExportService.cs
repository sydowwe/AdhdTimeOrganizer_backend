using AdhdTimeOrganizer.Reminders.application.dto.reminderDefinition;
using AdhdTimeOrganizer.Reminders.application.dto.reminderDispatch;
using AdhdTimeOrganizer.Reminders.application.export;

namespace AdhdTimeOrganizer.Reminders.domain.serviceContract;

/// <summary>
/// Renders the dashboard read DTOs to a downloadable file. Pure formatting over the exact DTOs the JSON
/// endpoints project, so the file can never drift from the API. <b>Ids only — no PII</b> (the module stores
/// none); recipients render as their user-id list. Mirrors Attendance's <c>IAttendanceExportService</c>, CSV only.
/// </summary>
public interface IReminderExportService
{
    /// <summary>The upcoming-reminders list rendered to file.</summary>
    ExportFile ExportUpcoming(IReadOnlyList<ReminderDefinitionDto> rows, ReminderExportFormat format);

    /// <summary>The dispatch-history (append-only ledger) rendered to file.</summary>
    ExportFile ExportDispatchHistory(IReadOnlyList<ReminderDispatchDto> rows, ReminderExportFormat format);
}