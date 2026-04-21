using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.activity;

namespace AdhdTimeOrganizer.domain.model.entity.activityHistory;

public class ActivityHistory : BaseEntityWithActivity
{
    public required DateTime StartTimestamp { get; set; }
    public required IntTime Length { get; set; }

    public DateTime EndTimestamp { get; set; }
}