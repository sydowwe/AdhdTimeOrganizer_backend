namespace AdhdTimeOrganizer.Common.application.dto.response.@base;

public record NameResponse : IdResponse
{
    public required string Name { get; init; }


}