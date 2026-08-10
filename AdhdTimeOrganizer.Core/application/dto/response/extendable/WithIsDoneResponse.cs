namespace AdhdTimeOrganizer.Core.application.dto.response.extendable;

public record WithIsDoneResponse : WithActivityResponse
{
    public bool IsDone { get; init; }
}