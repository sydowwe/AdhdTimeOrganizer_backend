using AdhdTimeOrganizer.ActivityProfiles.domain.model;

namespace AdhdTimeOrganizer.ActivityProfiles.domain.service;

/// <summary>
/// Whether a day fits a weather dependency — the whole of the leisure weather signal's judgement, in one pure
/// function, for the same reason <see cref="LeisureDrawRanker"/> is one: a rule that reads a database is a rule
/// nobody can check.
///
/// <para>The thresholds are deliberately generous. This signal only ever ranks an activity <b>up</b> and paints
/// a "good for today" badge — it never excludes anything — so a day wrongly called dry costs the user a slightly
/// odd suggestion, while a day wrongly called wet costs them the feature. When in doubt, fit.</para>
/// </summary>
public static class WeatherFitRule
{
    /// <summary>
    /// Drizzle. Below this a day is still "dry" to anyone deciding whether to go for a walk, and forecast totals
    /// this small are inside the provider's own noise.
    /// </summary>
    public const double DryPrecipitationMm = 1.0;

    /// <summary>Enough settled snow to be the point of the activity rather than an inconvenience.</summary>
    public const double SnowfallCm = 1.0;

    /// <summary>Half a short winter day. Below this "sunny" is being read off a forecast that mostly says cloud.</summary>
    public const double SunnyHours = 4.0;

    /// <summary>Sun on a freezing day is not what "wants sun" means; a light jacket is the line.</summary>
    public const double SunnyMinTemperatureC = 12.0;

    /// <summary>
    /// The codes today's weather fits. <see cref="WeatherDependencyCodes.None"/> is always in the set — a row
    /// meaning "any weather" fits every day by definition, including a day the provider called miserable.
    /// </summary>
    public static IReadOnlySet<string> MatchingCodes(DailyWeather weather)
    {
        var codes = new HashSet<string>(StringComparer.Ordinal) { WeatherDependencyCodes.None };

        // Snow arrives inside the precipitation total, so a snowy day is not a dry one — but it is exactly what a
        // "wants snow" activity was waiting for.
        var isDry = weather.PrecipitationMm <= DryPrecipitationMm;

        if (isDry)
            codes.Add(WeatherDependencyCodes.Dry);

        if (isDry && weather.SunshineHours >= SunnyHours && weather.MaxTemperatureC >= SunnyMinTemperatureC)
            codes.Add(WeatherDependencyCodes.Sunny);

        if (weather.SnowfallCm >= SnowfallCm)
            codes.Add(WeatherDependencyCodes.Snow);

        return codes;
    }
}
