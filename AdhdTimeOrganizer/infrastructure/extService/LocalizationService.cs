using System.Globalization;
using System.Reflection;
using System.Resources;
using AdhdTimeOrganizer.domain.serviceContract;

namespace AdhdTimeOrganizer.infrastructure.extService;

public class LocalizationService<TResource> : ILocalizationService
{
    /// <summary>
    /// Gets a localized string for the given key and resource class, allowing a specific culture override.
    /// </summary>
    public string GetString(string key, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentUICulture;
        var resourceManager = GetResourceManagerFromType();
        return resourceManager?.GetString(key, culture) ?? $"[[{key}]]";
    }

    private ResourceManager GetResourceManagerFromType()
    {
        var property = typeof(TResource).GetProperty("ResourceManager", BindingFlags.Static | BindingFlags.Public);
        if (property == null)
        {
            throw new InvalidOperationException($"The resource type {typeof(TResource).FullName} does not have a static public 'ResourceManager' property.");
        }

        return (ResourceManager)property.GetValue(null)!;
    }
}