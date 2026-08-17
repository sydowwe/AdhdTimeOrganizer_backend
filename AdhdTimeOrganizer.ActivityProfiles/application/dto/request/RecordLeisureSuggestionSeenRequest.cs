namespace AdhdTimeOrganizer.ActivityProfiles.application.dto.request;

/// <summary>
/// What became of a draw. The client sends <c>rejected</c> for the whole outgoing set when the user presses
/// "something else", and <c>committed</c> for the single one they planned.
/// </summary>
public record RecordLeisureSuggestionSeenRequest
{
    /// <summary><c>key</c> values from a previous draw — <c>"backlog:12"</c>, <c>"bucketList:8"</c>.</summary>
    public List<string> Keys { get; set; } = [];

    /// <summary><c>rejected</c> | <c>committed</c>.</summary>
    public string Outcome { get; set; } = null!;
}
