namespace AdhdTimeOrganizer.History.application.dto.request.history;

/// <summary><c>?activityId=</c>, bound from the query string. F2-07.</summary>
public record ActivityHistorySessionFitRequest
{
    public long ActivityId { get; init; }
}
