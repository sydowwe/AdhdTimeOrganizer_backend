using AdhdTimeOrganizer.Command.application.dto.response.activity;
using AdhdTimeOrganizer.Command.application.dto.response.extendable;

namespace AdhdTimeOrganizer.Command.application.dto.response.activityHistory;

public record WebExtensionDataResponse : WithActivityResponse
{
    public required string Domain { get; init; }
    public required string Title { get; init; }
    public required int Duration { get; init; }
    public required DateTime StartTimestamp { get; init; }
}