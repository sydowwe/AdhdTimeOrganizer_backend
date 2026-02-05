using FastEndpoints;

namespace AdhdTimeOrganizer.application.dto.request;

public record WebExtensionSummaryRequest
{
    [QueryParam]
    public DateTime From { get; set; }

    [QueryParam]
    public DateTime To { get; set; }

    [QueryParam]
    public int? WindowMinutes { get; set; }  // Re-aggregate: 10, 15, 30, 60

    [QueryParam]
    public int? MinSeconds { get; set; }  // Filter out domains below threshold
}