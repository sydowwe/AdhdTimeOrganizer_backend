using AdhdTimeOrganizer.application.dto.response.extendable;
using AdhdTimeOrganizer.domain.helper;

namespace AdhdTimeOrganizer.application.dto.response.activityHistory;

public record ActivityHistoryResponse : WithActivityResponse
{
    public required DateTime StartTimestamp { get; init; }
    public required MyIntTime Length { get; init; }
    public required DateTime EndTimestamp { get; init; }
}