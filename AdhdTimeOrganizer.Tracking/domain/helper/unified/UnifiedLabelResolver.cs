namespace AdhdTimeOrganizer.Tracking.domain.helper.unified;

/// <summary>
/// The one identity string a merged item carries, and the only place the cross-source join is decided.
///
/// <para><b>Why one string and not the pair the per-source dashboards carry.</b> Each per-source
/// contract has an identifier (<c>domain</c> / <c>processName</c> / <c>packageName</c>) and a display
/// name (<c>domain</c> / <c>productName</c> / <c>appLabel</c>); the three dashboards hash the
/// identifier for colour and print the display name, which is fine while a screen shows one source.
/// Merged it is not: <c>slack.exe</c> and <c>com.Slack</c> hash to different hues, so one application
/// arrives on one page under one name in two colours and the pie legend, the bar segments and the
/// timeline swatches each disagree. The unified response therefore carries exactly one string, and the
/// client derives display, selection key and colour from it.</para>
///
/// <para><b>What actually joins, and what deliberately does not.</b></para>
/// <list type="bullet">
/// <item>Desktop ↔ android joins on the display name, case-insensitively: <c>slack.exe</c> ships under
/// the product name <c>Slack</c> and <c>com.Slack</c> under the app label <c>Slack</c>, so the two
/// arrive as one item in one colour. This is the join only the server can do, because only the ledgers
/// hold the product and label strings.</item>
/// <item>A web-extension domain joins <b>nothing</b>. <c>github.com</c> and <c>Google Chrome</c> are
/// two items and two colours, and that is the correct outcome rather than a missed join — a merged day
/// showing both is right, and one showing no browser at all for an hour spent in a browser is not.
/// Likewise <c>youtube.com</c> in a browser and <c>YouTube</c> on a phone stay apart: inventing an
/// identity the data does not support would be worse than showing two.</item>
/// </list>
///
/// <para>Comparison is <see cref="StringComparer.OrdinalIgnoreCase"/> and the surviving spelling is the
/// one from the highest-precedence source that saw the item, so a label is stable across a request no
/// matter which ledger happened to be read first.</para>
/// </summary>
public static class UnifiedLabelResolver
{
    /// <summary>The domain, which is both identifier and display name on this source.</summary>
    public static string ForWebExtension(string domain) => Clean(domain);

    /// <summary>
    /// The product name, falling back to the process name. The fallback is not cosmetic: a blank
    /// product name shared by several processes would otherwise merge them all into one nameless item.
    /// </summary>
    public static string ForDesktop(string processName, string? productName) =>
        IsBlank(productName) ? Clean(processName) : Clean(productName!);

    /// <summary>The app label, falling back to the package name, for the same reason.</summary>
    public static string ForAndroid(string packageName, string? appLabel) =>
        IsBlank(appLabel) ? Clean(packageName) : Clean(appLabel!);

    /// <summary>
    /// The spelling every occurrence of an item should use, keyed by the case-folded label. Built
    /// across all sources at once so that the join happens before anything is grouped, summed or
    /// coloured — a canonicalisation applied per endpoint would let two surfaces disagree.
    /// </summary>
    public static Dictionary<string, string> BuildCanonicalSpellings(IEnumerable<SourceMinute> contributions)
    {
        var best = new Dictionary<string, (TrackingSource Source, string Label)>(StringComparer.OrdinalIgnoreCase);

        foreach (var contribution in contributions)
        {
            if (!best.TryGetValue(contribution.Label, out var current))
            {
                best[contribution.Label] = (contribution.Source, contribution.Label);
                continue;
            }

            if (contribution.Source < current.Source)
                best[contribution.Label] = (contribution.Source, contribution.Label);
        }

        return best.ToDictionary(kv => kv.Key, kv => kv.Value.Label, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    private static string Clean(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? "(unknown)" : trimmed;
    }
}
