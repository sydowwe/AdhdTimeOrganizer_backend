using AdhdTimeOrganizer.domain.model.entity.activity;

namespace AdhdTimeOrganizer.domain.model.entity.activityHistory;

public class WebExtensionData : BaseEntityWithActivity
{
    public required string Domain { get; set; }
    public required string Title { get; set; }
    public required int Duration { get; set; }
    public required DateTime StartTimestamp { get; set; }
}