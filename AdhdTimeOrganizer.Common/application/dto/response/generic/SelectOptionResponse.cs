using AdhdTimeOrganizer.Common.application.dto.response.@base;

namespace AdhdTimeOrganizer.Common.application.dto.response.generic;

public class SelectOptionResponse : IdResponse
{
    public required string Label { get; init; }
}