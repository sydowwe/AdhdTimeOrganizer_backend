namespace AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.android.dashboard;

/// <summary>
/// The android detail panel — the sibling of <c>DesktopProcessDetailsResponse</c>, over
/// <c>AndroidSessionData</c>'s real sessions rather than minute rows.
///
/// <para>Deliberately <b>no</b> <c>activeSeconds</c>/<c>backgroundSeconds</c>: the android source has
/// no foreground/background split, so total is total. <see cref="Devices"/> is the analogue of desktop's
/// monitor breakdown and <see cref="Days"/> of its window-title list.</para>
/// </summary>
public record AndroidAppDetailsResponse
{
    public required string PackageName { get; init; }

    /// <summary>The first non-empty label among the rows, falling back to the package name.</summary>
    public required string AppLabel { get; init; }

    public required long TotalSeconds { get; init; }

    /// <summary>Number of session rows behind the panel.</summary>
    public required int Sessions { get; init; }

    /// <summary>Start of the earliest session in the window.</summary>
    public required DateTime FirstUseUtc { get; init; }

    /// <summary>End of the latest session in the window — the last instant the app was in use.</summary>
    public required DateTime LastUseUtc { get; init; }

    /// <summary><see cref="TotalSeconds"/> over <see cref="Sessions"/>, rounded down.</summary>
    public required long MeanSessionSeconds { get; init; }

    public required long LongestSessionSeconds { get; init; }

    /// <summary>One entry per device that reported the app, ordered by <c>totalSeconds</c> descending.</summary>
    public required List<AndroidDeviceUsageDto> Devices { get; init; }

    /// <summary>
    /// One entry per calendar day <b>with rows</b>, on the user's clock, ordered by date. A day inside the
    /// span with no session for this app has no entry rather than a zeroed one.
    /// </summary>
    public required List<AndroidDayUsageDto> Days { get; init; }
}

/// <summary>
/// <see cref="DeviceId"/> is whatever the sync client sent as its identity — the sync payload carries no
/// friendlier name, so the client renders it verbatim.
/// </summary>
public record AndroidDeviceUsageDto
{
    public required string DeviceId { get; init; }
    public required long TotalSeconds { get; init; }
    public required int Sessions { get; init; }
}

public record AndroidDayUsageDto
{
    public required DateOnly Date { get; init; }
    public required long TotalSeconds { get; init; }
    public required int Sessions { get; init; }
}
