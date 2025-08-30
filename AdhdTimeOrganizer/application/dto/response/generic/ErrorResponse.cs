namespace AdhdTimeOrganizer.application.dto.response.generic;

public record ErrorResponse
{
    public required string Error { get; init; }
    public required string Message { get; init; }
}