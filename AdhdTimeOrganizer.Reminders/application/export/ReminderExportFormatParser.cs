namespace AdhdTimeOrganizer.Reminders.application.export;

/// <summary>
/// Parses the <c>?format=</c> query value for the dashboard export endpoints. Mirrors Attendance's
/// <c>AttendanceExportFormatParser</c> shape, but this module emits <b>CSV only</b>: Attendance's XLSX path is
/// Syncfusion (a licensed dependency) on a payroll-accountant handoff; the reminders dashboard exports are an
/// ops/audit dump, so a dependency-free CSV (mirroring EmployeeModule's <c>SimpleCsv</c>) keeps this domain-free
/// infra module's reference graph to just Kernel + Framework. Defaults to CSV when the value is omitted.
/// </summary>
public static class ReminderExportFormatParser
{
    /// <returns>The format, or <c>null</c> when the value is present but unsupported (caller returns 400).</returns>
    public static ReminderExportFormat? TryParse(string? raw)
    {
        return (raw ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" or "csv" => ReminderExportFormat.Csv,
            _ => null
        };
    }
}