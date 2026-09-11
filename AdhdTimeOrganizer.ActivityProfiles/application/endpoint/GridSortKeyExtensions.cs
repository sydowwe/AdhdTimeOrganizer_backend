using Sydowwe.Framework.application.dto.request.generic;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint;

/// <summary>
/// Rewrites the sort keys the profile grids send into members that exist on the projected responses.
/// </summary>
/// <remarks>
/// <para>
/// <c>BaseGridEndpoint</c> sorts the <b>projected response</b>, so what is sortable is decided by the
/// response DTO's shape, not by the model — and an unresolvable key is an <c>ArgumentException</c> out of
/// <c>SortByMany</c>, i.e. a 500 rather than an unsorted page. Every key the client can put in a column
/// header therefore has to land on a real member, and the ones that do not are remapped here.
/// </para>
/// <para>
/// <b>One caller today: the anchor grid's <c>periodKey</c>.</b> The three profile grids used this to
/// redirect <c>activity.name</c> onto a flattened <c>activityName</c>; they now nest the whole
/// <see cref="Core.application.dto.response.activity.ActivityResponse"/>, so that key resolves natively
/// and the remaps are gone. What is left is the case a remap is actually for — one client column that is
/// two members on the response, which no change of shape can make resolve on its own.
/// </para>
/// <para>
/// Lives in the slice rather than the framework because it is the only use in the solution
/// (<c>GridActivityCategoryEndpoint</c> does its own remap inline). If a second project needs it, it
/// belongs next to <c>PreprocessSortBy</c> in <c>Sydowwe.Framework</c>.
/// </para>
/// </remarks>
internal static class GridSortKeyExtensions
{
    /// <summary>
    /// Replaces every occurrence of <paramref name="clientKey"/> with one entry per name in
    /// <paramref name="members"/>, in order, leaving all other keys untouched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The direction is copied, never varied.</b> A composite column is one column to the client, which
    /// sends it one <c>isDesc</c>; each expanded member carries that same value, so
    /// <c>periodKey desc</c> is <c>anchor_year DESC, anchor_month DESC</c> and never a mix. Mixed
    /// directions remain reachable only by a client that sends the members as separate sort entries,
    /// which is it asking for them as separate columns.
    /// </para>
    /// <para>
    /// Order matters: <c>SortByMany</c> appends each key with <c>ThenBy</c>
    /// (<c>OrderQueryableExtensions.SmartOrderBy</c>), so the first member listed is the primary sort.
    /// </para>
    /// <para>
    /// Matched case-insensitively against the raw key, dots included — the client sends camelCase
    /// (<c>activity.name</c>) and <c>SortByMany</c> pascalizes per segment afterwards, so neither
    /// <c>Pascalize</c> nor a split belongs here.
    /// </para>
    /// </remarks>
    public static SortByRequest[] ExpandSortKey(this SortByRequest[] sortBy, string clientKey, params string[] members) =>
        sortBy.SelectMany(IEnumerable<SortByRequest> (s) => string.Equals(s.Key, clientKey, StringComparison.OrdinalIgnoreCase)
                ? members.Select(member => s with { Key = member })
                : [s])
            .ToArray();
}
