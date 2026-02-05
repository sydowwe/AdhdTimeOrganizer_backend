using FastEndpoints;

namespace AdhdTimeOrganizer.application.dto.request;

public record WebExtensionDailyTotalsRequest
{
    [QueryParam]
    public DateTime Date { get; set; }  // Just the date, will query full day
}
