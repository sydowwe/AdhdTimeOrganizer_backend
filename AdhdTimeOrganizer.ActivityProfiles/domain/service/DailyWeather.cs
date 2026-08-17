namespace AdhdTimeOrganizer.ActivityProfiles.domain.service;

/// <summary>
/// One day's forecast, reduced to the four numbers <see cref="WeatherFitRule"/> reads. A provider decides how
/// to obtain them; nothing downstream knows which provider ran, and swapping one for another cannot change the
/// rule as long as it can fill these in.
/// </summary>
/// <param name="PrecipitationMm">Total rain + showers + melted snow for the day.</param>
/// <param name="SnowfallCm">Snowfall for the day. Reported separately because "wants snow" and "wants dry" read the same precipitation total in opposite directions.</param>
/// <param name="MaxTemperatureC">The day's high.</param>
/// <param name="SunshineHours">Hours of direct sun. The one number that separates "not raining" from "actually nice out".</param>
public sealed record DailyWeather(
    double PrecipitationMm,
    double SnowfallCm,
    double MaxTemperatureC,
    double SunshineHours);
