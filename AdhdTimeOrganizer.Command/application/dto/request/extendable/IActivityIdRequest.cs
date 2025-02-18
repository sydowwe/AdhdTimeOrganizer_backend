using AdhdTimeOrganizer.Common.application.dto.request.@base;

namespace AdhdTimeOrganizer.Command.application.dto.request.extendable;

public interface IActivityIdRequest : IMyRequest
{
    public long ActivityId { get; init; }
}