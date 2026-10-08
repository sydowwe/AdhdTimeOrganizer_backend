using AdhdTimeOrganizer.Planning.domain.model.@enum;

namespace AdhdTimeOrganizer.Planning.application.dto;

/// <summary>
/// The camelCase spellings of <see cref="SkipReasonKey"/> on the wire — <c>"noTime"</c>, <c>"notRelevant"</c>,
/// <c>"noEnergy"</c> — matching the client's <c>BuiltInSkipReason</c> and its <c>planner.skipReason.*</c> label keys.
/// <para>
/// Strings on the DTOs rather than the enum, for the reason <c>LeisureSuggestionTokens</c> gives: the host's bare
/// <c>JsonStringEnumConverter</c> writes members verbatim (<c>"NoTime"</c>), and camelCasing it globally would
/// rewrite every enum in every response. The constants are also what the EF projections compare against, so
/// the spelling lives here once.
/// </para>
/// </summary>
public static class SkipReasonKeyTokens
{
    public const string NoTime = "noTime";
    public const string NotRelevant = "notRelevant";
    public const string NoEnergy = "noEnergy";

    /// <summary>FO5's fresh start. Read-only on the wire: <see cref="TryParse"/> refuses it, so the skip sheet cannot
    /// send it and the reason keeps meaning "closed because the user was away".</summary>
    public const string Away = "away";

    public static string? Token(SkipReasonKey? key) => key switch
    {
        null => null,
        SkipReasonKey.NoTime => NoTime,
        SkipReasonKey.NotRelevant => NotRelevant,
        SkipReasonKey.NoEnergy => NoEnergy,
        SkipReasonKey.Away => Away,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null)
    };

    /// <summary>
    /// Exact, case-sensitive — an unknown or mis-cased token is a 400, not a guess. Parses only the reasons a user
    /// can pick; <see cref="Away"/> is written by the server alone.
    /// </summary>
    public static bool TryParse(string? token, out SkipReasonKey key)
    {
        switch (token)
        {
            case NoTime:
                key = SkipReasonKey.NoTime;
                return true;
            case NotRelevant:
                key = SkipReasonKey.NotRelevant;
                return true;
            case NoEnergy:
                key = SkipReasonKey.NoEnergy;
                return true;
            default:
                key = default;
                return false;
        }
    }
}
