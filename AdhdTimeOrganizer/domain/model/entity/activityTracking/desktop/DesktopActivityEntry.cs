using AdhdTimeOrganizer.domain.model.entity.user;

namespace AdhdTimeOrganizer.domain.model.entity.activityHistory;

public sealed class DesktopActivityEntry : BaseEntityWithUser
{
    public required DateOnly RecordDate { get; set; }
    public required DateTime WindowStart { get; set; }

    public required string ProcessName { get; set; }
    public required string ProductName { get; init; }
    public string? WindowTitle { get; set; }
    public string? ExecutablePath { get; set; }

    public required bool IsFullscreen { get; init; }
    public required int ActiveSeconds { get; init; }
    public required int BackgroundSeconds { get; init; }
    public required bool IsPlayingSound { get; init; }

    public required int ActiveMonitor { get; set; }
}
