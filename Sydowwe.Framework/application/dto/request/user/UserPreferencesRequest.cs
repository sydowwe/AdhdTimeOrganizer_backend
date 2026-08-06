using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.@enum;

namespace Sydowwe.Framework.application.dto.request.user;

/// <summary>
/// The preference fields that live on <see cref="BaseUser"/>. Every field is nullable: absent means
/// "leave unchanged", so a client can PUT a single preference.
///
/// <para>Which preferences a settings screen actually exposes is a product decision, so hosts derive
/// this record to add their own columns and override
/// <c>BaseUpdateUserPreferencesEndpoint.ApplyExtraPreferences</c> to write them.</para>
/// </summary>
public record UserPreferencesRequest
{
    public AppThemeEnum? Theme { get; init; }
    public AvailableLocales? Locale { get; init; }

    /// <summary>IANA or system timezone id. Validated by <see cref="BaseUserPreferencesValidator{TRequest}"/>.</summary>
    public string? Timezone { get; init; }

    public bool? AskBeforeDelete { get; init; }
}