using System.Globalization;
using System.Text;
using AdhdTimeOrganizer.Scheduler.application.dto.scheduledJob;
using AdhdTimeOrganizer.Scheduler.application.dto.scheduledJobRun;
using AdhdTimeOrganizer.Scheduler.application.export;
using AdhdTimeOrganizer.Scheduler.domain.@enum;
using AdhdTimeOrganizer.Scheduler.domain.serviceContract;
using AdhdTimeOrganizer.Scheduler.infrastructure;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.helper;
using Syncfusion.XlsIO;

namespace AdhdTimeOrganizer.Scheduler.application.service;

/// <summary>
/// Renders the dashboard projections to XLSX (Syncfusion XlsIO, matching <c>AttendanceExportService</c>) or CSV
/// (semicolon-delimited, UTF-8 BOM so Excel detects the encoding and SK locale). Pure formatting over the
/// response DTOs — no querying. Technical infra columns only; the run payload snapshot is deliberately never
/// exported (it may carry PII the handler is responsible for).
/// </summary>
public class SchedulerExportService : ISchedulerExportService, ISingletonService
{
    // SK locale: decimal comma + dd.MM.yyyy dates, matching the existing Export* endpoints.
    private static readonly CultureInfo Sk = CultureInfo.GetCultureInfo("sk-SK");

    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string CsvContentType = "text/csv";

    public ExportFile ExportJobsOverview(IReadOnlyList<ScheduledJobDto> jobs, SchedulerExportFormat format)
    {
        var headers = new[]
        {
            "JobKey", "HandlerKey", "OwnerModule", "Description", "JobScheduleType", "Cron",
            "JobIntervalPreset", "IntervalCount", "MisfirePolicy", "DisallowConcurrent",
            "Status", "NextRunAt", "LastRunAt", "LastOutcome",
            "TimeZoneId", "MaxRetries", "AlertOnFailure", "RunAtUtc"
        };

        var rows = jobs.Select(j => new object?[]
        {
            j.JobKey, j.HandlerKey, j.OwnerModule, j.Description, j.ScheduleType.ToString(), j.Cron,
            j.IntervalPreset?.ToString(), j.IntervalCount, j.MisfirePolicy.ToString(), j.DisallowConcurrent,
            j.Status.ToString(), j.NextRunAt, j.LastRunAt, j.LastOutcome?.ToString(),
            j.TimeZoneId, j.MaxRetries, j.AlertOnFailure, j.RunAtUtc
        }).ToList();

        return Render(new Table(headers, rows), "Jobs", "scheduler-jobs", format);
    }

    public ExportFile ExportRunHistory(IReadOnlyList<ScheduledJobRunDto> runs, SchedulerExportFormat format)
    {
        var headers = new[]
        {
            "JobKey", "HandlerKey", "ScheduledFireTime", "StartedAt", "FinishedAt", "DurationMs",
            "Outcome", "ErrorType", "ErrorMessage", "TriggerSource", "CorrelationId", "ReplaysRunId"
        };

        var rows = runs.Select(r => new object?[]
        {
            r.JobKeySnapshot, r.HandlerKeySnapshot, r.ScheduledFireTime, r.StartedAt, r.FinishedAt, r.DurationMs,
            r.Outcome.ToString(), r.ErrorType, r.ErrorMessage, r.TriggerSource.ToString(), r.CorrelationId, r.ReplaysRunId
        }).ToList();

        return Render(new Table(headers, rows), "Runs", "scheduler-run-history", format);
    }

    // ── Format dispatch + writers (mirror AttendanceExportService) ────────────────────────────────

    private static ExportFile Render(Table table, string sheetName, string baseName, SchedulerExportFormat format)
    {
        return format switch
        {
            SchedulerExportFormat.Csv => new ExportFile(WriteCsv(table), CsvContentType, $"{baseName}.csv"),
            _ => new ExportFile(WriteXlsx(table, sheetName), XlsxContentType, $"{baseName}.xlsx")
        };
    }

    private static byte[] WriteXlsx(Table table, string sheetName)
    {
        SyncfusionLicense.EnsureRegistered();

        using var excelEngine = new ExcelEngine();
        var app = excelEngine.Excel;
        app.DefaultVersion = ExcelVersion.Xlsx;

        var workbook = app.Workbooks.Create(1);
        var sheet = workbook.Worksheets[0];
        sheet.Name = sheetName;

        for (var c = 0; c < table.Headers.Length; c++)
            sheet.Range[1, c + 1].Text = table.Headers[c];
        sheet.Range[1, 1, 1, table.Headers.Length].CellStyle.Font.Bold = true;

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            for (var c = 0; c < row.Length; c++)
            {
                var cell = sheet.Range[r + 2, c + 1];
                switch (row[c])
                {
                    case null:
                        break;
                    case long l:
                        cell.Number = l;
                        cell.NumberFormat = "0";
                        break;
                    case int i:
                        cell.Number = i;
                        cell.NumberFormat = "0";
                        break;
                    case bool b:
                        cell.Text = b ? "true" : "false";
                        break;
                    case DateTime dt:
                        cell.Text = dt.ToString("dd.MM.yyyy HH:mm:ss", Sk);
                        break;
                    default:
                        cell.Text = row[c]!.ToString();
                        break;
                }
            }
        }

        sheet.UsedRange.AutofitColumns();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] WriteCsv(Table table)
    {
        var sb = new StringBuilder();
        // Semicolon delimiter: the SK decimal comma would otherwise collide with a comma delimiter.
        sb.Append(string.Join(';', table.Headers.Select(CsvHelper.EscapeCsv))).Append("\r\n");

        foreach (var row in table.Rows)
            sb.Append(string.Join(';', row.Select(FormatCsvCell))).Append("\r\n");

        // BOM so Excel opens UTF-8 with the correct diacritics. GetBytes never emits the preamble itself
        // (the encoder flag only governs GetPreamble), so prepend it explicitly.
        var encoding = new UTF8Encoding(true);
        return [.. encoding.GetPreamble(), .. encoding.GetBytes(sb.ToString())];
    }

    private static string FormatCsvCell(object? value)
    {
        return CsvHelper.EscapeCsv(value switch
        {
            null => string.Empty,
            long l => l.ToString(Sk),
            int i => i.ToString(Sk),
            bool b => b ? "true" : "false",
            DateTime dt => dt.ToString("dd.MM.yyyy HH:mm:ss", Sk),
            _ => value.ToString() ?? string.Empty
        });
    }

    private static string EscapeCsv(string field)
    {
        // Neutralize CSV formula injection: a cell starting with one of these is
        // treated as a formula by Excel/LibreOffice. Prefix with ' to force text.
        if (field.Length > 0 && field[0] is '=' or '+' or '-' or '@' or '\t')
            field = "'" + field;

        if (field.IndexOfAny([';', '"', '\n', '\r']) < 0)
            return field;
        return $"\"{field.Replace("\"", "\"\"")}\"";
    }

    private sealed record Table(string[] Headers, List<object?[]> Rows);
}