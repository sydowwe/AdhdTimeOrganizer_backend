using AdhdTimeOrganizer.Reminders.application.export;
using FastEndpoints;

namespace AdhdTimeOrganizer.Reminders.application.endpoint.dashboard;

/// <summary>
/// Shared base for the dashboard <c>…/export</c> endpoints: owns the <c>?format=</c> parse-and-400 preamble so the
/// two concrete exporters can't drift on it. Concrete endpoints call <see cref="ResolveFormatOrFailAsync"/> first
/// and return when it yields <c>null</c> (the 400 has already been written).
/// </summary>
public abstract class BaseReminderExportEndpoint<TRequest> : Endpoint<TRequest>
    where TRequest : notnull
{
    /// <returns>
    /// The parsed format, or <c>null</c> when the supplied <c>?format=</c> value is unsupported — in which case a
    /// 400 with the standard error has already been sent and the caller must return.
    /// </returns>
    protected async Task<ReminderExportFormat?> ResolveFormatOrFailAsync(CancellationToken ct)
    {
        var format = ReminderExportFormatParser.TryParse(Query<string?>("format", false));
        if (format is null)
        {
            AddError("Unsupported export format. Use 'csv'.");
            await Send.ErrorsAsync(400, ct);
        }

        return format;
    }
}