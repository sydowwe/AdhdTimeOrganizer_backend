using AdhdTimeOrganizer.Command.application.dto.response.activity;
using AdhdTimeOrganizer.Command.application.dto.response.extendable;
using AdhdTimeOrganizer.Common.domain.helper;

namespace AdhdTimeOrganizer.Command.application.dto.response.activityHistory;

public record ActivityHistoryResponse : WithActivityResponse
{
    public required DateTime StartTimestamp { get; init; }
    public required MyIntTime Length { get; init; }
    public required DateTime EndTimestamp { get; init; }
}