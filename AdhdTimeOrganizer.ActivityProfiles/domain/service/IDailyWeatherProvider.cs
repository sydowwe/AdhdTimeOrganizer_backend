namespace AdhdTimeOrganizer.ActivityProfiles.domain.service;

/// <summary>
/// Today's weather for a free-text place, or <c>null</c> when it cannot be had.
///
/// <para><b>Never throws, and that is the contract.</b> An unknown place name, a provider outage, a timeout and
/// a disabled integration all come back as <c>null</c>: the caller is a suggestion ranking that must degrade to
/// "no weather opinion" rather than fail, and an implementation that threw would turn a third party's bad day
/// into a 500 on the picker.</para>
/// </summary>
public interface IDailyWeatherProvider
{
    /// <param name="location">Free text as the user typed it, e.g. <c>Bratislava, SK</c>.</param>
    Task<DailyWeather?> GetTodayAsync(string location, CancellationToken ct);
}
