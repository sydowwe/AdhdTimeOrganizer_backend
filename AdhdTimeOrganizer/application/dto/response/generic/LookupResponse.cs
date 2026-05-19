namespace AdhdTimeOrganizer.application.dto.response.generic;

public record LookupResponse : SelectOptionResponse
{
    public required int SortOrder { get; init; }
}
