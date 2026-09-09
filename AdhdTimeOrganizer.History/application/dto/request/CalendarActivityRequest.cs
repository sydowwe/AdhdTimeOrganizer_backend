namespace AdhdTimeOrganizer.History.application.dto.request.calendar;

public record CalendarActivityRequest
{
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }
    public int TopN { get; init; } = 3;
}