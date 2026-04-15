using System.Diagnostics.CodeAnalysis;

namespace AdhdTimeOrganizer.application.dto.dto;

public record TimeDto
{
    public required int Hours { get; init; }
    public required int Minutes { get; init; }

    [SetsRequiredMembers]
    public TimeDto(int hours, int minutes)
    {
        Hours = hours;
        Minutes = minutes;
    }
    public TimeOnly ToTimeOnly() => new(Hours, Minutes);
}