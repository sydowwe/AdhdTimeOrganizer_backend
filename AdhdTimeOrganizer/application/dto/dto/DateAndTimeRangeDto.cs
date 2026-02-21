namespace AdhdTimeOrganizer.application.dto.dto;

public record DateAndTimeRangeDto
{
    public required DateOnly Date { get; init; }
    
    public required TimeDto From { get; init; }
    public required TimeDto To { get; init; }

    public (DateTime From, DateTime To) ToDateTimeRange()
    {
        var from = Date.ToDateTime(new TimeOnly(From.Hours, From.Minutes))
            .ToUniversalTime();
        var to = Date.ToDateTime(new TimeOnly(To.Hours, To.Minutes))
            .ToUniversalTime();

        // If 'to' is before or equal to 'from', it spans over midnight, so add a day
        if (to <= from)
        {
            to = to.AddDays(1);
        }
        return (from, to);
    }
}