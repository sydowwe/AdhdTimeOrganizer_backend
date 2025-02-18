using AdhdTimeOrganizer.Common.application.dto.response.@base;

namespace AdhdTimeOrganizer.Common.application.dto.response.generic;

public record SelectOptionResponse: IdResponse
{
    public required string Text { get; init; }
}