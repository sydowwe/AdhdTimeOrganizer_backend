using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.domain.model.entity.user;

namespace AdhdTimeOrganizer.domain.model.entity.timer;

public class PomodoroTimerPreset : BaseEntityWithUser
{
    public required string Name { get; set; }
    public required int FocusDuration { get; set; }
    public required int ShortBreakDuration { get; set; }
    public required int LongBreakDuration { get; set; }

    public required int FocusPeriodInCycleCount { get; set; }
    public required int NumberOfCycles { get; set; }

    public long? FocusActivityId { get; set; }
    public virtual Activity? FocusActivity { get; set; }
    public long? RestActivityId { get; set; }
    public virtual Activity? RestActivity { get; set; }
}
