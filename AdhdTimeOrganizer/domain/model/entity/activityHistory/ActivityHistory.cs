using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.activity;

namespace AdhdTimeOrganizer.domain.model.entity.activityHistory;

public class ActivityHistory : BaseEntityWithActivity
{
    public required DateTime StartTimestamp { get; set; }
    public required MyIntTime Length { get; set; }

    public required DateTime EndTimestamp { get; set; }
}