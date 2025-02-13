using AdhdTimeOrganizer.Command.application.dto.response.activity;
using AdhdTimeOrganizer.Command.application.dto.response.extendable;
using AdhdTimeOrganizer.Common.application.dto.response.@base;

namespace AdhdTimeOrganizer.Command.application.dto.response.activityHistory;

public class AlarmResponse : NameTextColorResponse, IEntityWithActivityResponse
{
    public DateTime StartTimestamp { get; set; }
    public ActivityResponse Activity { get; set; }
    public bool IsActive { get; set; }
}