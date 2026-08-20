namespace AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking;

/// <summary>
/// The identity pair every dashboard item carries, under one pair of names on all three sources.
///
/// <para><b>Why, given each source already names its own item.</b> The three dashboards feed the
/// identifier to a colour hash and print the display name. That works while a screen shows one source,
/// but the fields are called <c>domain</c> / <c>processName</c> / <c>packageName</c> and
/// <c>domain</c> / <c>productName</c> / <c>appLabel</c>, so "the identifier" and "the display name" are
/// six field names and therefore three call sites on the client — three chances for one of them to
/// hash the display name by mistake and put an application on screen in two colours.</para>
///
/// <para><b>Purely additive.</b> The source-specific fields stay exactly as they are and keep their
/// meanings; these two are a second way to read the same strings, so nothing that reads the old names
/// changes. They are computed rather than stored so the two can never disagree.</para>
///
/// <para>Not to be confused with the unified dashboards' single <c>label</c>: merged, one identity
/// string is the point, because a key that differs per source is exactly what puts one application on
/// screen twice. Both are true at once — a per-source response carries the pair, the unified response
/// carries the joined single string.</para>
/// </summary>
public interface IDashboardItem
{
    /// <summary>
    /// The stable identifier — the domain, the process name, the package name. This is what a colour
    /// hash and a selection key should read, never <see cref="Label"/>: several processes ship under
    /// one product name, so keying on the display name merges them, and everything with a blank one
    /// merges into a single nameless item.
    /// </summary>
    string Key { get; }

    /// <summary>
    /// The display name, never blank — it falls back to <see cref="Key"/> where the source has no
    /// better string, so a client can print it without a fallback of its own.
    /// </summary>
    string Label { get; }
}

/// <summary>The one copy of the fallback rule <see cref="IDashboardItem.Label"/> promises.</summary>
public static class DashboardItem
{
    public static string LabelOr(string? display, string key) =>
        string.IsNullOrWhiteSpace(display) ? key : display;
}
