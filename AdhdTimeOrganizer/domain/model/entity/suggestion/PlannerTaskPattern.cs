using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.domain.model.entity.suggestion;

public class PlannerTaskPattern
{
    public long UserId { get; set; }
    public long ActivityId { get; set; }
    public Activity Activity { get; set; } = null!;
    public long? ImportanceId { get; set; }
    public TaskImportance? Importance { get; set; }
    public bool IsBackground { get; set; }
    public int PatternType { get; set; }
    public int PatternValue { get; set; }
    public int OccurrenceCount { get; set; }
    public TimeOnly AvgStartTime { get; set; }
    public TimeOnly AvgEndTime { get; set; }
}
