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

    // ---------------------------------------------------------------------------------------------
    // Column widths (CQ-9). Every one of these mirrors a HasMaxLength in the entity configuration:
    // without the rule, an over-long value is a Postgres 22001 the client sees as a 500 instead of a
    // 400 naming the field. They live here rather than beside each RuleFor so a column widened in a
    // migration has one place to be widened again.
    // ---------------------------------------------------------------------------------------------

    /// <summary>DesktopActivityEntry.ProcessName — <c>varchar(255)</c>.</summary>
    public const int MaxProcessNameLength = 255;

    /// <summary>DesktopActivityEntry.ProductName — <c>varchar(255)</c>.</summary>
    public const int MaxProductNameLength = 255;

    /// <summary>DesktopActivityEntry.WindowTitle — <c>varchar(1024)</c>. See also <see cref="MaxIndexedEntryTextBytes"/>.</summary>
    public const int MaxWindowTitleLength = 1024;

    /// <summary>
    /// DesktopActivityEntry.ExecutablePath. The column itself is unbounded — it is an encrypted column,
    /// so it is <c>text</c> holding ciphertext — but the value is still client-supplied and stored
    /// forever, and a Windows path cannot exceed 32 767 characters even in its long form.
    /// </summary>
    public const int MaxExecutablePathLength = 1024;

    /// <summary>
    /// The combined UTF-8 size of the two entry columns that sit in a btree index —
    /// <c>(user_id, window_start, record_date, process_name, window_title)</c>.
    /// </summary>
    /// <remarks>
    /// This is the quieter half of CQ-9 and the reason a plain <c>MaximumLength</c> is not enough:
    /// <c>WindowTitle</c> is <c>varchar(1024)</c>, which the column accepts at any width, but 1024
    /// three-byte characters is ~3 kB and Postgres refuses an index tuple over ~2704 bytes. That insert
    /// fails with a different error and the same 500 the length rules exist to prevent. The bound is on
    /// <em>bytes</em> because the limit is, and it is well under 2704 to leave room for the three fixed
    /// columns and the tuple's own overhead. The value is rejected rather than truncated: two window
    /// titles cut to the same prefix would collide under that unique index and be summed together by
    /// <c>CollapseCollidingEntries</c>, so a size limit would silently merge two distinct windows.
    /// </remarks>
    public const int MaxIndexedEntryTextBytes = 2000;

    /// <summary>TrackerDesktopMappingByPattern.ProcessName / ProductName — <c>varchar(200)</c>.</summary>
    public const int MaxDesktopMappingPatternLength = 200;

    /// <summary>TrackerDesktopMappingByPattern.WindowTitle — <c>varchar(255)</c>.</summary>
    public const int MaxDesktopMappingWindowTitleLength = 255;

    /// <summary>TrackerAndroidMappingByPattern.PackageName / AppLabel — <c>varchar(255)</c>.</summary>
    public const int MaxAndroidMappingPatternLength = 255;
}
