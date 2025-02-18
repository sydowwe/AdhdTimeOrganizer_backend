using AdhdTimeOrganizer.Command.application.dto.response.activity;
using AdhdTimeOrganizer.Common.application.dto.response.@base;

namespace AdhdTimeOrganizer.Command.application.dto.response.extendable;


public record WithActivityResponse : IdResponse, IEntityWithActivityResponse
{
    public required ActivityResponse Activity { get; init; }
}