namespace AdhdTimeOrganizer.Scheduler.domain.@enum;

/// <summary>File format for a dashboard export (mirrors the existing Export* endpoints' two formats).</summary>
public enum SchedulerExportFormat
{
    /// <summary>Excel workbook (.xlsx).</summary>
    Xlsx,

    /// <summary>Semicolon-delimited CSV (.csv), UTF-8 BOM so Excel opens it with the SK locale.</summary>
    Csv
}