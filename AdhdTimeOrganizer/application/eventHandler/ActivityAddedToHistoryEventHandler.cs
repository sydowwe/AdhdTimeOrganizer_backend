using AdhdTimeOrganizer.application.@event;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.eventHandler;

public class ActivityAddedToHistoryEventHandler(AppCommandDbContext dbContext, ILogger<ActivityAddedToHistoryEventHandler> logger) : IEventHandler<ActivityAddedToHistoryEvent>
{
    public async Task HandleAsync(ActivityAddedToHistoryEvent eventModel, CancellationToken ct)
    {
        var historyRecord = new ActivityHistory
        {
            UserId = eventModel.UserId,
            ActivityId = eventModel.ActivityId,
            StartTimestamp = eventModel.StartTimestamp,
            Length = eventModel.Length,
            EndTimestamp = eventModel.StartTimestamp.AddSeconds(eventModel.Length.TotalSeconds)
        };
        var result = await dbContext.AddEntityAsync(historyRecord, ct);
        if (result.Failed)
        {
            logger.LogError(result.ErrorMessage);
        }
    }
}