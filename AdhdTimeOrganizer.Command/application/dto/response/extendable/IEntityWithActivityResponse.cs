using AdhdTimeOrganizer.Command.application.dto.response.activity;
using AdhdTimeOrganizer.Common.application.dto.response.@base;

namespace AdhdTimeOrganizer.Command.application.dto.response.extendable;

public interface IEntityWithActivityResponse : IIdResponse
{
    public ActivityResponse Activity { get; init; }
}