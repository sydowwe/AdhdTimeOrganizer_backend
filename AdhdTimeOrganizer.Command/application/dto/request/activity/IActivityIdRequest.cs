using AdhdTimeOrganizer.Common.application.dto.request.@base;

namespace AdhdTimeOrganizer.Command.application.dto.request.activity;

public interface IActivityIdRequest : IMyRequest
{
    public long ActivityId { get; init; }
}