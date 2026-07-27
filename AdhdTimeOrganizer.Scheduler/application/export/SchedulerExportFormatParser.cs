using AdhdTimeOrganizer.Scheduler.domain.@enum;

namespace AdhdTimeOrganizer.Scheduler.application.export;

/// <summary>Parses the <c>?format=</c> query value for the dashboard export endpoints. Defaults to XLSX when omitted.</summary>
public static class SchedulerExportFormatParser
{
    /// <returns>The format, or <c>null</c> when the value is present but unsupported (caller returns 400).</returns>
    public static SchedulerExportFormat? TryParse(string? raw)
    {
        return (raw ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" or "xlsx" => SchedulerExportFormat.Xlsx,
            "csv" => SchedulerExportFormat.Csv,
            _ => null
        };
    }
}