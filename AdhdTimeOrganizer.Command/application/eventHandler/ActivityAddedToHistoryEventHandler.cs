using AdhdTimeOrganizer.Command.application.@interface.activityHistory;
using AdhdTimeOrganizer.Command.domain.@event;
using AdhdTimeOrganizer.Command.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;
using MediatR;

namespace AdhdTimeOrganizer.Command.application.eventHandler;

public class ActivityAddedToHistoryEventHandler(IActivityHistoryService historyService) : INotificationHandler<ActivityAddedToHistoryEvent>
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