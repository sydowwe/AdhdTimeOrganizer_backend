using AdhdTimeOrganizer.application.dto.request.extendable;

namespace AdhdTimeOrganizer.application.dto.request.activity;

public record ActivityIdRequest : IActivityIdRequest
{
    public long ActivityId { get; init; }
}