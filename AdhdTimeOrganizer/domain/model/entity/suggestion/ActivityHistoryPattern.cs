using AdhdTimeOrganizer.domain.model.entity.activity;

namespace AdhdTimeOrganizer.domain.model.entity.suggestion;

public class ActivityHistoryPattern
{
    public long UserId { get; set; }
    public long ActivityId { get; set; }
    public Activity Activity { get; set; } = null!;
    public int PatternType { get; set; }
    public int PatternValue { get; set; }
    public int OccurrenceCount { get; set; }
    public TimeOnly AvgStartTime { get; set; }
    public TimeOnly AvgEndTime { get; set; }
}
