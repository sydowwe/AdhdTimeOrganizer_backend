using AdhdTimeOrganizer.application.dto.request.activity;

namespace AdhdTimeOrganizer.application.dto.request.extendable;

public record WithIsDoneRequest : ActivityIdRequest
{
    public bool IsDone { get; init; }
}