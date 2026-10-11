using Sydowwe.Framework.application.dto.dto;
using Sydowwe.Framework.application.dto.request.user;

namespace AdhdTimeOrganizer.application.dto.request.user;

/// <summary>
/// Theme / Locale / Timezone / AskBeforeDelete come from <see cref="UserPreferencesRequest"/>;
/// <see cref="FirstDayOfWeek"/>, <see cref="WeatherLocation"/>, the default sleep window and the two morning-briefing
/// fields are this portal's own <c>User</c> columns.
/// Everything readable back through <c>AppUserDataResponse</c>.
/// </summary>
public record UpdateUserPreferencesRequest : UserPreferencesRequest
{
    public int? FirstDayOfWeek { get; init; }

    /// <summary>
    /// The morning briefing on or off. Off brings back the routine slice's own "suggested for today" digest, which the
    /// briefing otherwise replaces. Null leaves it unchanged.
    /// </summary>
    public bool? MorningBriefingEnabled { get; init; }

    /// <summary>Minutes after the day's wake-up the briefing goes out, 0 – 180. Null leaves it unchanged.</summary>
    public int? MorningBriefingOffsetMinutes { get; init; }

    /// <summary>
    /// Free text for "where I am", e.g. <c>Bratislava, SK</c> — the input the leisure weather signal geocodes.
    ///
    /// <para><b>Null and empty differ here, unlike every other field on this request.</b> The base's convention
    /// is "null means leave unchanged", which on its own would make a preference impossible to *clear*: a client
    /// sending null to unset it would be indistinguishable from one PUTting a single unrelated preference. So
    /// the empty string is the clear — send <c>""</c> to go back to having no location, and the column is
    /// stored as null.</para>
    /// </summary>
    public string? WeatherLocation { get; init; }

    /// <summary>
    /// FO10: when the user's day usually starts. Every calendar day they have not given a sleep window of its own —
    /// today and later — moves onto it (<c>ICalendarSleepWindowSync</c>), and every day created later starts with it.
    /// </summary>
    public TimeDto? DefaultWakeUpTime { get; init; }

    /// <summary>FO10: when the user's day usually ends. At or before <see cref="DefaultWakeUpTime"/> means after
    /// midnight; 00:00 is the default.</summary>
    public TimeDto? DefaultBedTime { get; init; }
}
