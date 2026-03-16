using AdhdTimeOrganizer.application.dto.response.generic;

namespace AdhdTimeOrganizer.application.dto.response.activity;

public record ActivityFilterFormResponse : SelectOptionResponse
{
    public required long RoleId { get; init; }
    public long? CategoryId { get; init; }
}