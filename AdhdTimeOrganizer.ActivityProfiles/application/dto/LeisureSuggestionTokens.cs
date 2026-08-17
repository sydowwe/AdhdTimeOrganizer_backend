using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;

namespace AdhdTimeOrganizer.ActivityProfiles.application.dto;

/// <summary>
/// The camelCase spellings the leisure endpoints put on the wire, and the parsers for the two the client
/// sends back.
///
/// <para><b>Why strings rather than enums on these DTOs.</b> The host registers a bare
/// <c>JsonStringEnumConverter</c>, which writes enum members verbatim — <c>"Low"</c>, <c>"ReadyToStart"</c> —
/// while this contract is specified in camelCase throughout (<c>"low"</c>, <c>"readyToStart"</c>,
/// <c>"bucketList"</c>) and the cards index their translations on exactly those tokens. Mapping here rather
/// than reconfiguring the serializer keeps the change to this one contract: the ambient enum format is what
/// every other endpoint in the app already emits, and quietly camelCasing it globally would rewrite the
/// wire format of every response in the solution.</para>
///
/// <para>The mapping is explicit for a second reason: renaming a C# enum member is then not a breaking API
/// change, and a new member cannot slip onto the wire unspelled — <see cref="Token(EnergyLevel)"/> and its
/// siblings throw rather than guess.</para>
/// </summary>
public static class LeisureSuggestionTokens
{
    public static string Token(EnergyLevel level) => level switch
    {
        EnergyLevel.Low => "low",
        EnergyLevel.Medium => "medium",
        EnergyLevel.High => "high",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
    };

    public static bool TryParseEnergy(string? token, out EnergyLevel level)
    {
        switch (token)
        {
            case "low":
                level = EnergyLevel.Low;
                return true;
            case "medium":
                level = EnergyLevel.Medium;
                return true;
            case "high":
                level = EnergyLevel.High;
                return true;
            default:
                level = default;
                return false;
        }
    }

    public static string? Token(EffortType? effort) => effort switch
    {
        null => null,
        EffortType.Physical => "physical",
        EffortType.Mental => "mental",
        _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, null)
    };

    /// <summary>
    /// <c>NeedsShopping</c> deliberately has no spelling: it can never reach a draw — the eligibility rule
    /// drops it — and giving it one would invite a card that renders "needs shopping" as a reason to do
    /// something now.
    /// </summary>
    public static string Token(ReadinessStatus status) => status switch
    {
        ReadinessStatus.Planning => "planning",
        ReadinessStatus.ReadyToStart => "readyToStart",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    public static bool TryParseOutcome(string? token, out LeisureSuggestionOutcome outcome)
    {
        switch (token)
        {
            case "rejected":
                outcome = LeisureSuggestionOutcome.Rejected;
                return true;
            case "committed":
                outcome = LeisureSuggestionOutcome.Committed;
                return true;
            default:
                outcome = default;
                return false;
        }
    }
}
