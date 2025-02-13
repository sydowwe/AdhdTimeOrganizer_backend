using AdhdTimeOrganizer.Command.application.dto.response.extendable;
using AdhdTimeOrganizer.Common.domain.helper;

namespace AdhdTimeOrganizer.Command.application.dto.response.activityHistory;

public class ActivityHistoryResponse : WithActivityResponse
{
    public DateTime StartTimestamp { get; set; }
    public MyIntTime Length { get; set; }
    public DateTime EndTimestamp { get; set; }
}