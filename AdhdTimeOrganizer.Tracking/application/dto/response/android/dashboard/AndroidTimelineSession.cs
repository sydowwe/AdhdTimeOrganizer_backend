namespace AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.android.dashboard;

public record AndroidTimelineSession
{
    public long Id { get; set; }
    public required string PackageName { get; init; }
    public required string AppLabel { get; init; }
    public required DateTime StartedAt { get; init; }
    public required DateTime EndedAt { get; init; }

    /// <summary>Wall-clock length of this session, <c>EndedAt - StartedAt</c>.</summary>
    public required long DurationSeconds { get; init; }

    /// <summary>
    /// Tracked activity seconds inside this session, matching <c>TimelineSession.TotalSeconds</c> on
    /// the other two sources — where a one-minute window can hold fewer than sixty active seconds, so
    /// the two differ.
    ///
    /// <para><b>On android it always equals <see cref="DurationSeconds"/>.</b> The ledger stores real
    /// foreground sessions and the sync endpoint rejects any whose duration disagrees with its own
    /// bounds by more than two seconds, so there is no idle remainder inside a session to subtract.
    /// </para>
    ///
    /// <para>It previously carried the <i>whole span's</i> total for the app label, repeated on every
    /// one of that app's sessions — so the timeline tooltip's "Active time" read as a day total on
    /// every row, and any client averaging it over sessions was multiplying by the session count. The
    /// field is kept rather than dropped because the client reads it on all three sources.</para>
    /// </summary>
    public required long TotalSeconds { get; init; }
}
