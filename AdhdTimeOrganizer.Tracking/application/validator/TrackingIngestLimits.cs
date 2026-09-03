namespace AdhdTimeOrganizer.Tracking.application.validator;

/// <summary>
/// The bounds every ingest route enforces on an untrusted client payload (SEC-5,
/// <c>review/tracking/02-findings.md</c>). All three ledgers are append-only and written straight from a
/// desktop agent, a browser extension or a phone, so an unbounded payload is stored permanently.
/// </summary>
/// <remarks>
/// The session-length bound is the load-bearing one. <c>UnifiedActivityLoader.SliceIntoMinutes</c>
/// allocates one <c>SourceMinute</c> per minute of an android session, so a single accepted decade-long
/// row would make every unified dashboard for that user allocate millions of records — a stored,
/// permanent denial of service that stays invisible in the android dashboards, which read the same row
/// as one session. The loader clamps defensively as well; this is the gate that stops the row existing.
/// </remarks>
public static class TrackingIngestLimits
{
    /// <summary>
    /// Sessions accepted in one android sync. A device syncing after a long offline stretch uploads its
    /// whole backlog, so this is far above a normal day's worth rather than snug.
    /// </summary>
    public const int MaxAndroidSessionsPerSync = 1000;

    /// <summary>
    /// The longest android session that can be stored. A phone reporting more than a day of continuous
    /// use of one package is a broken clock or a forged payload, not usage.
    /// </summary>
    public const int MaxAndroidSessionSeconds = 24 * 60 * 60;

    /// <summary>Entries in one desktop heartbeat — a heartbeat covers a single minute.</summary>
    public const int MaxDesktopEntriesPerWindow = 200;

    /// <summary>Activities in one web-extension heartbeat — likewise a single minute.</summary>
    public const int MaxWebExtensionActivitiesPerHeartbeat = 200;

    /// <summary>
    /// How far ahead of the server's clock a client instant may sit before it is rejected. Device clocks
    /// drift and the sync is asynchronous, so a small window is normal; hours are not.
    /// </summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The longest client-supplied instant <em>string</em> an ingest route accepts (CQ-22). The android sync
    /// takes its two instants as text and logs them verbatim when they fail to parse, so they are bounded
    /// before they can reach a log line.
    /// </summary>
    /// <remarks>
    /// An ISO-8601 instant with an offset is 33 characters; this leaves room for any format a client might
    /// reasonably send and none for a payload. Unlike the session bounds — which drop one row so a backlog
    /// upload is not stranded by it — this is a 400 for the whole sync: a multi-kilobyte timestamp is a
    /// broken or hostile client, not a row a phone could plausibly produce.
    /// </remarks>
    public const int MaxInstantStringLength = 64;
}
