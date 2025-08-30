using AdhdTimeOrganizer.application.@event;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.eventHandler;

public class ActivityAddedToHistoryEventHandler(IActivityHistoryService historyService) : IEventHandler<ActivityAddedToHistoryEvent>
{
    public async Task Handle(ActivityAddedToHistoryEvent notification, CancellationToken cancellationToken)
    {
        var historyRecord = new ActivityHistory
        {
            ActivityId = notification.NewActivityHistory.ActivityId,
            StartTimestamp = notification.NewActivityHistory.StartTimestamp,
            Length = notification.NewActivityHistory.Length
        };
        await historyService.InsertAsync(historyRecord);
    }
}