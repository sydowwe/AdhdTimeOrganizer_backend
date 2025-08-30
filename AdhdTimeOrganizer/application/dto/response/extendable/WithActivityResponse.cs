using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.extendable;


public record WithActivityResponse : IdResponse, IEntityWithActivityResponse
{
    public required ActivityResponse Activity { get; init; }
}