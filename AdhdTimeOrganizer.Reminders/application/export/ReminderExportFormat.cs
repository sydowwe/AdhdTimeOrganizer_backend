namespace AdhdTimeOrganizer.Reminders.application.export;

/// <summary>
/// File format for a reminders dashboard export. <b>CSV only</b> — see <see cref="ReminderExportFormatParser"/>
/// for why this domain-free infra module deliberately ships no XLSX (Syncfusion) path. The enum exists so a
/// future XLSX variant slots in without changing the endpoint shape.
/// </summary>
public enum ReminderExportFormat
{
    /// <summary>UTF-8 (BOM) CSV — dependency-free, opens in Excel.</summary>
    Csv
}