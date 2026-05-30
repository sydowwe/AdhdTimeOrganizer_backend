using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.activity;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;

namespace AdhdTimeOrganizer.application.dto.request.history;

public record ActivityHistoryRequest : ActivityIdRequest, IMyRequest<ActivityHistory>
{
    [Required]
    public required DateTime StartTimestamp { get; init; }

    [Required]
    public required IntTime Length { get; init; }

    public ActivityHistory ToEntity => new()
    {
        UserId = 0,
        ActivityId = ActivityId,
        StartTimestamp = StartTimestamp,
        Length = Length,
        EndTimestamp = StartTimestamp.AddSeconds(Length.TotalSeconds),
    };

    public void UpdateEntity(ActivityHistory e)
    {
        e.ActivityId = ActivityId;
        e.StartTimestamp = StartTimestamp;
        e.Length = Length;
    }
}
