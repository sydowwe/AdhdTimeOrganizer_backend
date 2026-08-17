using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AdhdTimeOrganizer.ActivityProfiles.domain.service;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AdhdTimeOrganizer.ActivityProfiles.infrastructure.extService.weather;

/// <summary>
/// The one outbound HTTP call any slice in this solution makes: Open-Meteo's free forecast, reached in two
/// steps because the user gives us a place name and the forecast wants coordinates.
///
/// <para><b>Open-Meteo because it needs no API key.</b> Nothing here has to be provisioned, rotated or kept out
/// of source control, which is what makes the whole feature deployable as a default-on setting. Swapping
/// providers means writing another <see cref="IDailyWeatherProvider"/> and changing one DI line — the rule
/// (<see cref="WeatherFitRule"/>) sees only <see cref="DailyWeather"/> and does not move.</para>
///
/// <para><b>Everything is a null, never an exception.</b> A place that does not geocode, a 500 from the
/// provider, a timeout, malformed JSON — all of them mean "no weather opinion today". The interface says so and
/// the endpoint depends on it: the picker must draw whether or not a third party is having a good day.</para>
///
/// <para><b>Nothing here logs the location.</b> It is the user's town, it is personal data, and log files
/// survive a GDPR erasure. Failures are logged by kind alone.</para>
/// </summary>
public class OpenMeteoWeatherProvider(
    HttpClient httpClient,
    IMemoryCache cache,
    IOptions<LeisureWeatherOptions> options,
    ILogger<OpenMeteoWeatherProvider> logger) : IDailyWeatherProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DailyWeather?> GetTodayAsync(string location, CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(location))
            return null;

        var normalized = location.Trim();

        try
        {
            var coordinates = await GeocodeAsync(normalized, settings, ct);
            if (coordinates is null)
                return null;

            return await ForecastAsync(coordinates.Value, settings, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller went away; not a provider problem and not worth a log line.
            throw;
        }
        catch (Exception exception)
        {
            // Includes the HttpClient's own timeout, which surfaces as a TaskCanceledException with the request's
            // token rather than ours — hence the ordering of these two catches.
            logger.LogDebug(exception, "Weather lookup failed; the leisure draw runs without a weather signal");
            return null;
        }
    }

    /// <summary>
    /// Place name → coordinates, cached for a month. Cached on the <b>raw text</b> the user typed, so two users
    /// in the same town who spelled it differently each pay one lookup and neither can see the other's key.
    /// </summary>
    private async Task<(double Latitude, double Longitude)?> GeocodeAsync(
        string location, LeisureWeatherOptions settings, CancellationToken ct)
    {
        var key = $"leisure-weather:geocode:{location.ToLowerInvariant()}";
        if (cache.TryGetValue<(double, double)?>(key, out var cached))
            return cached;

        var url = $"{settings.GeocodingUrl}?name={Uri.EscapeDataString(location)}&count=1&format=json";
        var result = await GetJsonAsync<GeocodingResponse>(url, ct);
        var match = result?.Results?.FirstOrDefault();

        // A miss is cached too. A user who typed nonsense would otherwise re-ask the provider on every draw, and
        // the answer is not going to change until they edit the setting.
        (double, double)? coordinates = match is null ? null : (match.Latitude, match.Longitude);
        cache.Set(key, coordinates, TimeSpan.FromHours(settings.GeocodeCacheHours));
        return coordinates;
    }

    /// <summary>
    /// Coordinates → today's numbers, cached per coordinate pair so everyone in one town shares one call.
    ///
    /// <para><c>timezone=auto</c> makes "today" mean the day it is <i>there</i>, which is the only reading that
    /// makes sense for "is it nice out right now". The cache entry is therefore also capped at the next local
    /// midnight — a plain TTL would keep serving yesterday's forecast into the small hours.</para>
    /// </summary>
    private async Task<DailyWeather?> ForecastAsync(
        (double Latitude, double Longitude) coordinates, LeisureWeatherOptions settings, CancellationToken ct)
    {
        var latitude = coordinates.Latitude.ToString("0.####", CultureInfo.InvariantCulture);
        var longitude = coordinates.Longitude.ToString("0.####", CultureInfo.InvariantCulture);

        var key = $"leisure-weather:forecast:{latitude},{longitude}";
        if (cache.TryGetValue<DailyWeather?>(key, out var cached))
            return cached;

        var url = $"{settings.ForecastUrl}?latitude={latitude}&longitude={longitude}"
                  + "&daily=temperature_2m_max,precipitation_sum,snowfall_sum,sunshine_duration"
                  + "&timezone=auto&forecast_days=1";

        var response = await GetJsonAsync<ForecastResponse>(url, ct);
        var daily = response?.Daily;
        if (daily is null)
            return null;

        var weather = new DailyWeather(
            First(daily.PrecipitationSum),
            First(daily.SnowfallSum),
            First(daily.Temperature2mMax),
            // The API reports sunshine in seconds; the rule thinks in hours, as a person would.
            First(daily.SunshineDuration) / 3600d);

        cache.Set(key, weather, CacheDuration(settings, response!.UtcOffsetSeconds));
        return weather;
    }

    private static TimeSpan CacheDuration(LeisureWeatherOptions settings, int utcOffsetSeconds)
    {
        var configured = TimeSpan.FromMinutes(settings.ForecastCacheMinutes);
        var localNow = DateTime.UtcNow.AddSeconds(utcOffsetSeconds);
        var untilLocalMidnight = localNow.Date.AddDays(1) - localNow;
        return untilLocalMidnight < configured ? untilLocalMidnight : configured;
    }

    /// <summary>A missing or empty daily array reads as zero, which the rule treats as a calm, cold, sunless day.</summary>
    private static double First(double[]? values) => values is { Length: > 0 } ? values[0] : 0;

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct) where T : class
    {
        using var response = await httpClient.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogDebug("Weather provider answered {StatusCode}", (int)response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct);
    }

    // ─── the provider's wire shapes ──────────────────────────────────────────────────────────────────

    private sealed class GeocodingResponse
    {
        public GeocodingResult[]? Results { get; init; }
    }

    private sealed class GeocodingResult
    {
        public double Latitude { get; init; }
        public double Longitude { get; init; }
    }

    private sealed class ForecastResponse
    {
        [JsonPropertyName("utc_offset_seconds")]
        public int UtcOffsetSeconds { get; init; }

        public DailyBlock? Daily { get; init; }
    }

    /// <summary>
    /// Open-Meteo returns each daily variable as its own array rather than a row per day, so with
    /// <c>forecast_days=1</c> every one of these holds exactly one element.
    /// </summary>
    private sealed class DailyBlock
    {
        [JsonPropertyName("temperature_2m_max")]
        public double[]? Temperature2mMax { get; init; }

        [JsonPropertyName("precipitation_sum")]
        public double[]? PrecipitationSum { get; init; }

        [JsonPropertyName("snowfall_sum")]
        public double[]? SnowfallSum { get; init; }

        [JsonPropertyName("sunshine_duration")]
        public double[]? SunshineDuration { get; init; }
    }
}
