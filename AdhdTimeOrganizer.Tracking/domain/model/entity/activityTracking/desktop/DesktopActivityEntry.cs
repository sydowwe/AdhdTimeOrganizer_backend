using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Sydowwe.Framework.domain.audit;

namespace AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;

public sealed class DesktopActivityEntry : BaseEntityWithUser
{
    private DateTime _windowStart;

    public DateOnly RecordDate { get; private set; }

    public required DateTime WindowStart
    {
        get => _windowStart;
        init
        {
            _windowStart = value;
            RecordDate = DateOnly.FromDateTime(value);
        }
    }

    public required string ProcessName { get; set; }
    public required string ProductName { get; init; }

    [AuditIgnore]
    public required string WindowTitle { get; set; }

    [AuditIgnore]
    public required string ExecutablePath { get; set; }

    public required bool IsFullscreen { get; init; }
    public required int ActiveSeconds { get; init; }
    public required int BackgroundSeconds { get; init; }
    public required bool IsPlayingSound { get; init; }

    public required int ActiveMonitor { get; set; }
}