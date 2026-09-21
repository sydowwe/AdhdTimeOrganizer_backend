namespace AdhdTimeOrganizer.Tracking.domain.helper;

/// <summary>
/// The single sentinel every tracking dashboard uses for the row it folds the sub-threshold tail into.
/// </summary>
/// <remarks>
/// It goes in the row's <em>identity</em> field — domain, process name, product name, package name, app
/// label, unified label — never in a display field, because the client keys off it: it translates the row
/// (<c>activityTracking.common.other</c>), colours it neutrally and refuses to make it selectable. A human
/// word like <c>"Other"</c> would render untranslated, drill down into a detail request for an app that
/// does not exist, and collide with the web dashboard's own client-side fold. The leading underscore is
/// what keeps it apart from a real domain or app that is genuinely called "other".
/// <para>
/// Every endpoint that folds emits this same value. If a display label is ever wanted on the wire, add it
/// as a separate field rather than putting a word here.
/// </para>
/// </remarks>
public static class FoldedRow
{
    public const string Sentinel = "_other";
}
