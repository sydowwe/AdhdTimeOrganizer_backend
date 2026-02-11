namespace AdhdTimeOrganizer.application.dto.dto;

public record DateTimeRangeDto
{
    public DateTime From { get; init; }
    public DateTime To { get; init; }
}