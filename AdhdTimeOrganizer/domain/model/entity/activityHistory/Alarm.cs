using AdhdTimeOrganizer.domain.model.entity.activity;

namespace AdhdTimeOrganizer.domain.model.entity.activityHistory;

public class Alarm : BaseEntityWithActivity
{
    public DateTime StartTimestamp { get; set; }
    public bool IsActive { get; set; } = true;
}