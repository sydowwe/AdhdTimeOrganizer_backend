using AdhdTimeOrganizer.Core.application.dto.request.extendable;

namespace AdhdTimeOrganizer.Core.application.dto.request.activity;

public record ActivityIdRequest : IActivityIdRequest
{
    public long ActivityId { get; init; }
}