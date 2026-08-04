using AdhdTimeOrganizer.application.dto.request.activity;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using Sydowwe.Framework.application.dto.request.@interface;
using Sydowwe.Framework.domain.valueObject;

namespace AdhdTimeOrganizer.application.dto.request.history;

public record ActivityHistoryRequest : ActivityIdRequest, IMyRequest<ActivityHistory>
{
    public required DateTime StartTimestamp { get; init; }


    public required IntTime Length { get; init; }

    public ActivityHistory ToEntity => new()
    {
        UserId = 0,
        ActivityId = ActivityId,
        StartTimestamp = StartTimestamp,
        Length = Length,
        EndTimestamp = StartTimestamp.AddSeconds(Length.TotalSeconds)
    };

    public void UpdateEntity(ActivityHistory e)
    {
        e.ActivityId = ActivityId;
        e.StartTimestamp = StartTimestamp;
        e.Length = Length;
    }
}