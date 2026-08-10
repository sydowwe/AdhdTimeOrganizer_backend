using AdhdTimeOrganizer.Core.application.dto.response.activity;
using Sydowwe.Framework.application.dto.response.@base;

namespace AdhdTimeOrganizer.Core.application.dto.response.extendable;

public record WithActivityResponse : IdResponse, IEntityWithActivityResponse
{
    public required ActivityResponse Activity { get; init; }
}