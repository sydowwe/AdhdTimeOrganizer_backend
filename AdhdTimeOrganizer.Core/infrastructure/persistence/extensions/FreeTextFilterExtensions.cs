namespace AdhdTimeOrganizer.Core.infrastructure.persistence.extensions;

/// <summary>
/// Pattern building for the grids' free-text filters, which run as <c>ILIKE</c> rather than
/// <c>string.Contains</c>.
///
/// <para><b>Why not <c>Contains</c>.</b> It translates to <c>LIKE '%x%'</c>, which Postgres evaluates
/// case-<em>sensitively</em> — typing <c>deep</c> into the name filter finds nothing when the row says
/// <c>Deep Work</c>. Neither shape can use a b-tree index, so the switch costs nothing at the plan
/// level and fixes the search.</para>
///
/// <para><b>Why the escape.</b> EF escapes LIKE metacharacters for you inside <c>Contains</c>; handing
/// <c>EF.Functions.ILike</c> an interpolated pattern does not. Unescaped, a user typing <c>%</c> or
/// <c>_</c> gets a wildcard instead of the literal they typed.</para>
///
/// <para>⚠ <b>The escape character has to be passed to <c>ILike</c> explicitly</b>, via
/// <see cref="EscapeCharacter"/> — the two-argument overload emits <c>ILIKE @pattern ESCAPE ''</c>,
/// and an <em>empty</em> ESCAPE turns the mechanism off rather than falling back to Postgres' default
/// backslash. Every <c>\%</c> this class produces then means "a literal backslash followed by any
/// text", so a filter containing <c>%</c>, <c>_</c> or <c>\</c> matches <b>nothing at all</b> — an
/// empty grid with no error, which is the same symptom as the case-sensitivity bug the <c>ILIKE</c>
/// switch was made to fix. <c>GridTextFilterTests</c> pins it.</para>
/// </summary>
public static class FreeTextFilterExtensions
{
    /// <summary>
    /// The <c>ESCAPE</c> character every <c>ILike</c> call over a <see cref="ToContainsPattern"/>
    /// pattern must pass as its third argument. A single backslash, matching what the escaping below
    /// inserts.
    /// </summary>
    public const string EscapeCharacter = "\\";

    /// <summary>
    /// Turns caller-supplied search text into a contains-anywhere <c>ILIKE</c> pattern.
    /// Call it <b>outside</b> the <c>Where</c> lambda — the result is a parameter, not something EF
    /// should try to translate.
    /// </summary>
    public static string ToContainsPattern(this string value) =>
        $"%{value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
}
