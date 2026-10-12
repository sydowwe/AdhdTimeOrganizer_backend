using AdhdTimeOrganizer.ActivityProfiles.domain.service;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.ActivityProfiles.application.service;

/// <summary>
/// The one place that turns a user into today's matching weather codes. Pulled out of
/// <c>GetLeisureWeatherFitEndpoint</c> when the draw started needing the same answer (F2-04), so the badge and
/// the ranking cannot disagree about what today's weather is.
/// </summary>
/// <remarks>
/// <para>
/// The provider promises never to throw; the <c>catch</c> here is what makes "the signal is never an error"
/// true of this service rather than merely of the implementation currently registered. The location stays out
/// of the log line — it is the user's town.
/// </para>
/// <para>
/// A run-out <paramref name="maxWait"/> answers <c>null</c>, which makes a draw taken during a slow provider
/// rank as if there were no weather. The provider caches today's forecast, and the picker fires
/// <c>/leisure-weather-fit</c> alongside the draw, so the next draw of the same seed finds the cache warm and
/// ranks with it — the one place the draw's "same body, same cards" promise bends, and only on a cold cache.
/// </para>
/// </remarks>
public sealed class LeisureWeatherSignal(
    DbContext dbContext,
    IDailyWeatherProvider weatherProvider,
    ILogger<LeisureWeatherSignal> logger) : ILeisureWeatherSignal, IScopedService
{
    public async Task<IReadOnlySet<string>?> TodayMatchingCodesAsync(long userId, TimeSpan? maxWait, CancellationToken ct)
    {
        // User is not IEntityWithUser and carries no global query filter, so the id predicate is the scoping.
        var location = await dbContext.Set<User>()
            .Where(u => u.Id == userId)
            .Select(u => u.WeatherLocation)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(location))
            return null;

        DailyWeather? weather;
        try
        {
            var fetch = weatherProvider.GetTodayAsync(location, ct);
            weather = maxWait is { } wait ? await fetch.WaitAsync(wait, ct) : await fetch;
        }
        catch (TimeoutException)
        {
            logger.LogInformation("The weather provider did not answer in time; user {UserId} gets no weather opinion on this draw", userId);
            return null;
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(exception,
                "The weather provider threw rather than returning no signal; user {UserId} gets no weather opinion today",
                userId);
            return null;
        }

        return weather is null ? null : WeatherFitRule.MatchingCodes(weather);
    }
}
