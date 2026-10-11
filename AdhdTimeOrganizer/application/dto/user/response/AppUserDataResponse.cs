using System.Diagnostics.CodeAnalysis;
using Sydowwe.Framework.application.dto.dto;
using Sydowwe.Framework.application.dto.response.user;

namespace AdhdTimeOrganizer.application.dto.response.user;

/// <summary>
/// <see cref="UserDataResponse"/> plus this portal's own preference columns — the read half of what
/// <c>UpdateUserPreferencesRequest</c> writes. The two must stay in step: a preference the client
/// can PUT but never read back is one it cannot render a settings control for.
/// </summary>
public record AppUserDataResponse : UserDataResponse
{
    /// <summary>
    /// Copies the base fields the framework endpoint has already filled in. <c>SetsRequiredMembers</c> because
    /// the inherited copy constructor sets every required member of the base, which the compiler cannot see.
    /// </summary>
    [SetsRequiredMembers]
    public AppUserDataResponse(UserDataResponse copy) : base(copy)
    {
    }

    public int FirstDayOfWeek { get; init; }

    /// <summary>Free text, or null when the user has not set one. See <c>User.WeatherLocation</c>.</summary>
    public string? WeatherLocation { get; init; }

    /// <summary>FO10: the user's default sleep window. See <c>User.DefaultWakeUpTime</c>.</summary>
    public required TimeDto DefaultWakeUpTime { get; init; }

    /// <inheritdoc cref="DefaultWakeUpTime"/>
    public required TimeDto DefaultBedTime { get; init; }

    /// <summary>See <c>User.MorningBriefingEnabled</c>.</summary>
    public bool MorningBriefingEnabled { get; init; }

    /// <summary>See <c>User.MorningBriefingOffsetMinutes</c>.</summary>
    public int MorningBriefingOffsetMinutes { get; init; }
}
