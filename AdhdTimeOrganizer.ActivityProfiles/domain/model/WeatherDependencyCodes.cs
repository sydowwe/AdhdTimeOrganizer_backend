namespace AdhdTimeOrganizer.ActivityProfiles.domain.model;

/// <summary>
/// The four weather meanings an <see cref="entity.ActivityWeatherDependency"/> row can carry, as stable
/// tokens.
///
/// <para>These are the same four the client already has locale keys for
/// (<c>enums.weatherDependency.{sunny,snow,dry,none}</c>), spelled identically so they cross the wire without
/// a translation table. Two contracts carry them: the weather endpoint resolves them into lookup <b>ids</b>,
/// and every backlog row carries its own <see cref="Effective"/> code as <c>weatherDependencyCode</c> — the
/// latter so the picker can tell a "don't care" row, which fits every day, from one that really depends on
/// the weather.</para>
///
/// <para>Not an enum, and deliberately: the value is a persisted column on a user-editable row, so it has to
/// survive a member being renamed or removed. An unrecognised code is simply a row that matches no weather.</para>
/// </summary>
public static class WeatherDependencyCodes
{
    /// <summary>Fits any weather — the row a user picks for "indoors, don't care".</summary>
    public const string None = "none";

    /// <summary>Wants sun: dry, bright and not freezing.</summary>
    public const string Sunny = "sunny";

    /// <summary>Only needs it not to rain.</summary>
    public const string Dry = "dry";

    /// <summary>Wants snow on the ground.</summary>
    public const string Snow = "snow";

    /// <summary>
    /// The code a row actually means: its stored <paramref name="code"/> when it has one, else whatever
    /// <see cref="Infer"/> reads off its label, else null. The one rule every reader applies — the weather-fit
    /// endpoint and the backlog row's <c>WeatherDependencyCode</c> must never disagree about the same row.
    ///
    /// <para>The stored code always wins, so renaming a seeded row does not change what it means.</para>
    /// </summary>
    public static string? Effective(string? code, string? text) => code ?? Infer(text);

    /// <summary>
    /// Best-effort guess at a code from a row's label, for rows that carry none — anything the user created
    /// themselves, plus anything seeded before the column existed and renamed before the backfill ran.
    ///
    /// <para>The guess is never stored. Persisting it would turn a heuristic into a fact the user cannot see or
    /// correct, and would then be wrong forever if they renamed the row again; re-running it per read costs
    /// nothing on a table of a handful of rows.</para>
    ///
    /// <para>English and Slovak only, matching the two locales the app ships. A row in any other language
    /// returns null and takes no part in the day's matching set, which the client reads as "no opinion" — the
    /// quiet degradation this whole signal is built around.</para>
    /// </summary>
    public static string? Infer(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Order matters: "no snow required" would match Snow first if Snow were tested before None.
        if (ContainsAny(text, "none", "any", "anything", "irrelevant", "indoor",
                "žiadn", "ziadn", "hociak", "vnútor", "vnutor", "vnútri", "vnutri"))
            return None;
        if (ContainsAny(text, "sun", "sunny", "sunshine", "clear", "nice", "warm",
                "slnko", "slneč", "slnec", "jasno"))
            return Sunny;
        if (ContainsAny(text, "snow", "snowy", "sneh", "snež", "sneží", "snezi",
                "zima", "zimný", "zimny", "zimné", "zimne"))
            return Snow;
        if (ContainsAny(text, "dry", "no rain", "sucho", "suché", "suche", "suchý", "suchy",
                "bez dažďa", "bez dazda"))
            return Dry;

        return null;
    }

    /// <summary>
    /// Every needle has to start where a word starts, and the short ones have to <b>be</b> the word.
    ///
    /// <para>A plain <c>Contains</c> read "Any<b>thing</b> such as a walk" as <i>dry</i> and "<b>Sun</b>day only"
    /// as <i>sunny</i> — a confident wrong badge on someone's own row, which is worse than the "no opinion" an
    /// unrecognised label gets. Five characters is the line because that is where the Slovak stems start being
    /// unambiguous (<c>slneč-</c>, <c>such-é</c>), so they can still match an inflected ending, while <c>sun</c>,
    /// <c>any</c> and <c>dry</c> only match standing alone. Longer forms of the short words are listed
    /// explicitly above rather than reached by prefix.</para>
    /// </summary>
    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(needle => Matches(text, needle));

    private static bool Matches(string text, string needle)
    {
        var mustBeWholeWord = needle.Length < 5;

        for (var index = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
             index >= 0;
             index = text.IndexOf(needle, index + 1, StringComparison.OrdinalIgnoreCase))
        {
            if (index > 0 && char.IsLetter(text[index - 1]))
                continue;

            var end = index + needle.Length;
            if (!mustBeWholeWord || end == text.Length || !char.IsLetter(text[end]))
                return true;
        }

        return false;
    }
}
