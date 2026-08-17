namespace AdhdTimeOrganizer.ActivityProfiles.infrastructure.extService.weather;

/// <summary>
/// Settings for the leisure weather signal. Bound in the host's <c>Program.cs</c> — an unbound
/// <c>IOptions&lt;T&gt;</c> still resolves, so a missing binding would run on defaults rather than fail.
/// </summary>
public class LeisureWeatherOptions
{
    public const string SectionName = "LeisureWeather";

    /// <summary>
    /// Turns the outbound calls off without unregistering anything: the provider then returns null for every
    /// location and the endpoint answers "no signal". The switch a deployment reaches for when the provider is
    /// misbehaving, and what the integration tests would use if they ever ran the real provider.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Open-Meteo's free geocoding search. No API key; see https://open-meteo.com/en/docs/geocoding-api.</summary>
    public string GeocodingUrl { get; set; } = "https://geocoding-api.open-meteo.com/v1/search";

    /// <summary>Open-Meteo's free forecast. No API key; see https://open-meteo.com/en/docs.</summary>
    public string ForecastUrl { get; set; } = "https://api.open-meteo.com/v1/forecast";

    /// <summary>
    /// How long a resolved forecast is reused. Three hours is well inside how often a daily summary actually
    /// changes, and holds the whole app to a handful of calls per user per day.
    /// </summary>
    public int ForecastCacheMinutes { get; set; } = 180;

    /// <summary>
    /// How long a place name's coordinates are reused. Towns do not move, so this is long on purpose — it is a
    /// cache, not a store, and losing it costs one extra call.
    /// </summary>
    public int GeocodeCacheHours { get; set; } = 720;
}
