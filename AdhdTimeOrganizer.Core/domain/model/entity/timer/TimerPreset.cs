using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;

namespace AdhdTimeOrganizer.Core.domain.model.entity.timer;

public class TimerPreset : BaseEntityWithUser
{
    public required int Duration { get; set; }
    public long? ActivityId { get; set; }
    public virtual Activity? Activity { get; set; }
}