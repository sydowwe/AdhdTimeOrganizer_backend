using Sydowwe.Framework.application.dto.response.generic;

namespace AdhdTimeOrganizer.Core.application.dto.response.activity;

public record ActivityFilterFormResponse : SelectOptionResponse
{
    public required long RoleId { get; init; }
    public long? CategoryId { get; init; }
}