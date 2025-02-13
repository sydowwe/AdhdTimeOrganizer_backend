using AdhdTimeOrganizer.Common.application.dto.response.@base;

namespace AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;

public class TimePeriodResponse : TextColorResponse
{
    public int LengthInDays { get; set; }
    public bool IsHiddenInView { get; set; }
}