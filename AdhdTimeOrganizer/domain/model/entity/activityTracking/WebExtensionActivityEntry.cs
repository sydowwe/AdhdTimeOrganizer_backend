using AdhdTimeOrganizer.domain.model.entity.user;

namespace AdhdTimeOrganizer.domain.model.entity.activityHistory;

public class WebExtensionActivityEntry : BaseEntityWithUser
{
    public required DateOnly RecordDate { get; set; }
    public required DateTime WindowStart { get; set; }  // Always 1-min aligned
    public required string Domain { get; set; }
    public required string? Url { get; set; }
    public required int ActiveSeconds { get; set; }
    public required int BackgroundSeconds { get; set; }
    public required bool IsFinal { get; set; }
}