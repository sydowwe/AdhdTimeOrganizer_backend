namespace AdhdTimeOrganizer.ActivityProfiles.domain.service;

/// <summary>
/// Today's weather for a user, as the set of <see cref="model.WeatherDependencyCodes"/> it fits — or <c>null</c>
/// when there is no signal at all. Both leisure consumers read it: <c>GET /leisure-weather-fit</c> turns the codes
/// into the caller's lookup ids for the badge, and the draw feeds them to <see cref="LeisureDrawRanker"/>.
/// </summary>
/// <remarks>
/// <b><c>null</c> and "nothing fits" are different answers, and only the first is possible from a missing input.</b>
/// No location, a place that does not geocode, a provider outage, a provider that throws, or a wait that runs out
/// all answer <c>null</c>: no weather opinion, so nothing is excluded and nothing ranks up. A real answer always
/// contains <see cref="model.WeatherDependencyCodes.None"/> (see <see cref="WeatherFitRule.MatchingCodes"/>), so a
/// non-null set is never empty.
/// </remarks>
public interface ILeisureWeatherSignal
{
    /// <param name="userId">Whose <c>WeatherLocation</c> to read.</param>
    /// <param name="maxWait">
    /// How long the caller is willing to wait on the provider before treating it as no signal. <c>null</c> waits
    /// as long as the provider's own timeout. The draw passes a short bound so the first cards never wait on a
    /// third party for long.
    /// </param>
    Task<IReadOnlySet<string>?> TodayMatchingCodesAsync(long userId, TimeSpan? maxWait, CancellationToken ct);
}
