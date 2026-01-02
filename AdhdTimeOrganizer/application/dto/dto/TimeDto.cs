namespace AdhdTimeOrganizer.application.dto.dto;

public record TimeDto
{
    public int Hours { get; init; }
    public int Minutes { get; init; }
}