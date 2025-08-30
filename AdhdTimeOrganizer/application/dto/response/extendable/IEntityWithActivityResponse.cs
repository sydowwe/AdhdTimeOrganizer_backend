using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.extendable;

public interface IEntityWithActivityResponse : IIdResponse
{
    public ActivityResponse Activity { get; init; }
}