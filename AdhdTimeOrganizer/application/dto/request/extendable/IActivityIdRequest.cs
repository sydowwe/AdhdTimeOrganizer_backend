using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.extendable;

public interface IActivityIdRequest : IMyRequest
{
    public long ActivityId { get; init; }
}