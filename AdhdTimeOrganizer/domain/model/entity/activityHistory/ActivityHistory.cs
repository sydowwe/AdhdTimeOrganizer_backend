using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Sydowwe.Framework.domain.valueObject;

namespace AdhdTimeOrganizer.domain.model.entity.activityHistory;

public class ActivityHistory : BaseEntityWithActivity
{
    public required DateTime StartTimestamp { get; set; }
    public required IntTime Length { get; set; }

    public DateTime EndTimestamp { get; set; }
}