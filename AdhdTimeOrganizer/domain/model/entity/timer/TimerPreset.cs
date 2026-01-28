using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.domain.model.entity.user;

namespace AdhdTimeOrganizer.domain.model.entity.timer;

public class TimerPreset : BaseEntityWithUser
{
    public required int Duration { get; set; }
    public long? ActivityId { get; set; }
    public virtual Activity? Activity { get; set; }
}
