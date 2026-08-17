using Sydowwe.Framework.application.dto.request.user;

namespace AdhdTimeOrganizer.application.dto.request.user;

/// <summary>
/// Theme / Locale / Timezone / AskBeforeDelete come from <see cref="UserPreferencesRequest"/>;
/// <see cref="FirstDayOfWeek"/> and <see cref="WeatherLocation"/> are this portal's own <c>User</c> columns.
/// Everything readable back through <c>AppUserDataResponse</c>.
/// </summary>
public record UpdateUserPreferencesRequest : UserPreferencesRequest
{
    public int? FirstDayOfWeek { get; init; }

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
}
