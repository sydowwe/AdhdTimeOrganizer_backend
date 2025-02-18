using AdhdTimeOrganizer.Command.application.dto.request.activity;

namespace AdhdTimeOrganizer.Command.application.dto.request.extendable;

public record WithIsDoneRequest : ActivityIdRequest
{
    public bool IsDone { get; init; }
}