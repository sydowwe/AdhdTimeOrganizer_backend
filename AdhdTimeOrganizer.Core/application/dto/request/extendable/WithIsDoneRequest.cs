using AdhdTimeOrganizer.Core.application.dto.request.activity;

namespace AdhdTimeOrganizer.Core.application.dto.request.extendable;

public record WithIsDoneRequest : ActivityIdRequest
{
    public bool IsDone { get; init; }
}