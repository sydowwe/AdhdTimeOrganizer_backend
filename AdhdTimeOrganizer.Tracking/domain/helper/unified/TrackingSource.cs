namespace AdhdTimeOrganizer.Tracking.domain.helper.unified;

/// <summary>
/// The three ingest ledgers, <b>in precedence order</b> — the enum's numeric order is level 2 of the
/// overlap rule (<c>webExtension &gt; desktop &gt; android</c>) and every comparison in
/// <see cref="UnifiedMinuteMerger"/> reads it that way. Reordering the members silently reorders the
/// rule.
///
/// <para>Web extension over desktop is the substantive half: an hour in a browser is one
/// <c>chrome.exe</c> process to the desktop agent and a set of domains to the extension, and keeping
/// the process would discard the only information the overlap carries. Desktop over android is a
/// tie-break between two machines that rarely collide, not a claim that one tracker is truer.</para>
/// </summary>
public enum TrackingSource
{
    WebExtension = 0,
    Desktop = 1,
    Android = 2
}

/// <summary>
/// The wire names of <see cref="TrackingSource"/>.
///
/// <para><b>Spelled out here rather than left to the enum serializer.</b> The host registers a plain
/// <c>JsonStringEnumConverter</c> with no naming policy, so an enum on the wire would be
/// <c>"WebExtension"</c> while the client reads <c>"webExtension"</c> — a mismatch that costs nothing
/// to compile and shows up as an empty filter. Parsing here also lets an unknown member come back as a
/// validation message naming the three legal values instead of a JSON parse failure.</para>
/// </summary>
public static class TrackingSourceNames
{
    public const string WebExtension = "webExtension";
    public const string Desktop = "desktop";
    public const string Android = "android";

    /// <summary>All three, in the precedence order of <see cref="TrackingSource"/>.</summary>
    public static readonly IReadOnlyList<TrackingSource> All =
        [TrackingSource.WebExtension, TrackingSource.Desktop, TrackingSource.Android];

    public const string AllowedMessage = "sources may only contain webExtension, desktop or android";

    public static string ToWireName(this TrackingSource source) => source switch
    {
        TrackingSource.WebExtension => WebExtension,
        TrackingSource.Desktop => Desktop,
        TrackingSource.Android => Android,
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

    /// <summary>Case-sensitive on purpose: the client sends one of three literals and a typo in a shared link should fail loudly.</summary>
    public static bool TryParse(string? value, out TrackingSource source)
    {
        switch (value)
        {
            case WebExtension:
                source = TrackingSource.WebExtension;
                return true;
            case Desktop:
                source = TrackingSource.Desktop;
                return true;
            case Android:
                source = TrackingSource.Android;
                return true;
            default:
                source = default;
                return false;
        }
    }

    /// <summary>
    /// The request's <c>sources</c> as a set. Duplicates collapse and the order the caller sent is
    /// discarded — the contract says to treat it as a set however it was built.
    /// </summary>
    public static IReadOnlySet<TrackingSource> ParseSet(IEnumerable<string>? values)
    {
        var set = new HashSet<TrackingSource>();

        foreach (var value in values ?? [])
            if (TryParse(value, out var source))
                set.Add(source);

        return set;
    }
}
