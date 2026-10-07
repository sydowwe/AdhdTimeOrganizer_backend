using AdhdTimeOrganizer.Planning.application.dto.@enum;

namespace AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;

/// <summary>Body of <c>POST /calendar/day-plan/{date}/replan/apply</c> (FO4).</summary>
public record ApplyDayReplanRequest
{
    /// <summary>
    /// The <c>previewToken</c> of the preview being applied, echoed back unchanged. The server recomputes the plan
    /// from it against the day as it now stands; a day that changed since the preview no longer matches (409).
    /// </summary>
    public required string PreviewToken { get; init; }

    /// <summary>
    /// One decision per block in the preview's <c>wontFit</c> — exactly those blocks, no more and no fewer. Empty
    /// when everything fit.
    /// </summary>
    public List<DayReplanWontFitDecision> WontFit { get; init; } = [];
}

/// <summary>What to do with one block the re-plan had no room for.</summary>
public record DayReplanWontFitDecision
{
    public required long TaskId { get; init; }
    public required ReplanWontFitResolution Resolution { get; init; }
}
