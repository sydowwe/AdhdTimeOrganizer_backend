namespace AdhdTimeOrganizer.ActivityProfiles.domain.model;

/// <summary>
/// The four weather meanings an <see cref="entity.ActivityWeatherDependency"/> row can carry, as stable
/// tokens.
///
/// <para>These are the same four the client already has locale keys for
/// (<c>enums.weatherDependency.{sunny,snow,dry,none}</c>). They never cross the wire — the weather endpoint
/// resolves them into lookup <b>ids</b> precisely so the client never has to reason about them — but keeping
/// the spelling identical is what lets a future contract expose them without a translation table.</para>
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
        if (ContainsAny(text, "none", "any", "irrelevant", "indoor", "žiadn", "ziadn", "hociak", "vnútor", "vnutor"))
            return None;
        if (ContainsAny(text, "sun", "clear", "nice", "warm", "slnk", "slneč", "slnec", "jasno"))
            return Sunny;
        if (ContainsAny(text, "snow", "sneh", "snez", "snež", "zim"))
            return Snow;
        if (ContainsAny(text, "dry", "no rain", "such", "bez dažďa", "bez dazda"))
            return Dry;

        return null;
    }

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
