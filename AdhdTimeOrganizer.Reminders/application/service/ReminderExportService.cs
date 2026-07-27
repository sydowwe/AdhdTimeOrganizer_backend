using System.Globalization;
using System.Text;
using AdhdTimeOrganizer.Reminders.application.dto.reminderDefinition;
using AdhdTimeOrganizer.Reminders.application.dto.reminderDispatch;
using AdhdTimeOrganizer.Reminders.application.export;
using AdhdTimeOrganizer.Reminders.domain.serviceContract;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Reminders.application.service;

/// <summary>
/// Renders the dashboard read DTOs to CSV (UTF-8 BOM, RFC-4180 escaping — the dependency-free style of
/// EmployeeModule's <c>SimpleCsv</c>). Instants are written as round-trip ISO-8601 (this is a machine/audit
/// dump, not a localised payroll sheet). Pure projection over the already-queried DTOs, no DB access, no PII.
/// </summary>
public class ReminderExportService : IReminderExportService, ISingletonService
{
    private const string CsvContentType = "text/csv";

    public ExportFile ExportUpcoming(IReadOnlyList<ReminderDefinitionDto> rows, ReminderExportFormat format)
    {
        var headers = new[]
        {
            "Id", "OwnerModule", "SubjectType", "SubjectId", "Kind", "ScheduleType", "Status",
            "NextOccurrenceAt", "TemplateKey", "NotificationType", "RecipientMode", "RecipientUserIds", "DigestKey"
        };

        var data = rows.Select(r => new[]
        {
            Num(r.Id),
            r.OwnerModule,
            r.SubjectType,
            r.SubjectId,
            r.Kind,
            r.ScheduleType.ToString(),
            r.Status.ToString(),
            Instant(r.NextOccurrenceAt),
            r.TemplateKey,
            r.NotificationType?.ToString() ?? string.Empty,
            r.RecipientMode.ToString(),
            string.Join('|', r.Recipients.Select(x => x.UserId.ToString(CultureInfo.InvariantCulture))),
            r.DigestKey ?? string.Empty
        });

        return new ExportFile(WriteCsv(headers, data), CsvContentType, $"reminders-upcoming-{Stamp()}.csv");
    }

    public ExportFile ExportDispatchHistory(IReadOnlyList<ReminderDispatchDto> rows, ReminderExportFormat format)
    {
        var headers = new[]
        {
            "Id", "ReminderDefinitionId", "OccurrenceAt", "DispatchedAt", "Outcome", "SkipReason",
            "NotificationTypeSnapshot", "TemplateKeySnapshot", "RecipientsSnapshot", "CorrelationId", "ReversesDispatchId"
        };

        var data = rows.Select(r => new[]
        {
            Num(r.Id),
            Num(r.ReminderDefinitionId),
            Instant(r.OccurrenceAt),
            Instant(r.DispatchedAt),
            r.Outcome.ToString(),
            r.SkipReason?.ToString() ?? string.Empty,
            r.NotificationTypeSnapshot?.ToString() ?? string.Empty,
            r.TemplateKeySnapshot ?? string.Empty,
            r.RecipientsSnapshot,
            r.CorrelationId,
            r.ReversesDispatchId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
        });

        return new ExportFile(WriteCsv(headers, data), CsvContentType, $"reminders-dispatch-history-{Stamp()}.csv");
    }

    private static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Instant(DateTimeOffset? value) => value?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Stamp() => DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

    private static byte[] WriteCsv(IEnumerable<string> headers, IEnumerable<IEnumerable<string>> rows)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(',', headers.Select(Escape))).Append("\r\n");
        foreach (var row in rows)
            sb.Append(string.Join(',', row.Select(Escape))).Append("\r\n");

        // BOM so Excel opens UTF-8 with the correct characters. GetBytes never emits the preamble itself
        // (the encoder flag only governs GetPreamble), so prepend it explicitly.
        var encoding = new UTF8Encoding(true);
        return [.. encoding.GetPreamble(), .. encoding.GetBytes(sb.ToString())];
    }

    private static string Escape(string? value)
    {
        value ??= string.Empty;
        if (value.IndexOfAny([',', '"', '\n', '\r']) < 0)
            return value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}