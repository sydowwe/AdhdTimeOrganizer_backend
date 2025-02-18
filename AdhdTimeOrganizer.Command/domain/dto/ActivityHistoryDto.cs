using AdhdTimeOrganizer.Common.domain.helper;

namespace AdhdTimeOrganizer.Command.domain.dto;

public class ActivityHistoryCreatedDto
{
    public required long ActivityId { get; init; }
    public required DateTime StartTimestamp { get; init; }
    public required MyIntTime Length { get; init; }
}