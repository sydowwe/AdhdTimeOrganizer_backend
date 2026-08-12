namespace AdhdTimeOrganizer.History.application.dto.request.history;

public record ActivityHistoryAggregateByActivityRequest
{
    /// <summary>
    /// The activity ids to aggregate over. The caller batches the ids currently visible in a rendered
    /// to-do list into one request rather than issuing one request per item.
    /// </summary>
    public required List<long> ActivityIds { get; init; } = [];
}
