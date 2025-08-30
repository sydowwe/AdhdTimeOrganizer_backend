using System.Globalization;

namespace AdhdTimeOrganizer.domain.serviceContract;

public interface ILocalizationService
{
    /// <summary>
    /// Gets a localized string for the given key and resource class, allowing a specific culture override.
    /// </summary>
    string GetString(string key, CultureInfo? culture = null);
}