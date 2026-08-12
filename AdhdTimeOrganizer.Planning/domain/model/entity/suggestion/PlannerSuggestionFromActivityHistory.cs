using AdhdTimeOrganizer.Core.domain.model.entity.activity;

namespace AdhdTimeOrganizer.Planning.domain.model.entity.suggestion;

public class PlannerSuggestionFromActivityHistory
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