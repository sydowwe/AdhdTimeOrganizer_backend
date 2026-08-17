using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;

namespace AdhdTimeOrganizer.ActivityProfiles.domain.model;

/// <summary>
/// The identity of a leisure candidate as one string — <c>"&lt;source&gt;:&lt;activityId&gt;"</c>, e.g.
/// <c>"bucketList:8"</c>.
/// <para>
/// It is a wire format (the client keys its cards on it and sends it back to record an outcome) and at
/// the same time the value the draw's jitter hashes, which is why it lives in the domain rather than
/// next to the DTOs: the seeded draw is only reproducible while both ends spell the key identically.
/// The tokens are camelCase to match the rest of the leisure contract, and are not derived from the
/// enum member names — renaming <see cref="LeisureSuggestionSource"/> must not silently change what a
/// stored draw history refers to.
/// </para>
/// </summary>
public static class LeisureSuggestionKey
{
    private const string BacklogToken = "backlog";
    private const string ProjectToken = "project";
    private const string BucketListToken = "bucketList";

    public static string Token(LeisureSuggestionSource source) => source switch
    {
        LeisureSuggestionSource.Backlog => BacklogToken,
        LeisureSuggestionSource.Project => ProjectToken,
        LeisureSuggestionSource.BucketList => BucketListToken,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
    };

    public static bool TryParseSource(string token, out LeisureSuggestionSource source)
    {
        switch (token)
        {
            case BacklogToken:
                source = LeisureSuggestionSource.Backlog;
                return true;
            case ProjectToken:
                source = LeisureSuggestionSource.Project;
                return true;
            case BucketListToken:
                source = LeisureSuggestionSource.BucketList;
                return true;
            default:
                source = default;
                return false;
        }
    }

    public static string Format(LeisureSuggestionSource source, long activityId) =>
        $"{Token(source)}:{activityId}";

    /// <summary>
    /// Case-sensitive on the source token on purpose: a key is echoed back verbatim from a previous
    /// draw, so anything else is a caller that built it by hand and should be told rather than guessed at.
    /// </summary>
    public static bool TryParse(string? key, out LeisureSuggestionSource source, out long activityId)
    {
        source = default;
        activityId = 0;

        if (string.IsNullOrEmpty(key))
            return false;

        var separator = key.IndexOf(':');
        if (separator <= 0 || separator == key.Length - 1)
            return false;

        return TryParseSource(key[..separator], out source)
               && long.TryParse(key[(separator + 1)..], out activityId)
               && activityId > 0;
    }
}
