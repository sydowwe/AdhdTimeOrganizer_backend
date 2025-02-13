using AdhdTimeOrganizer.Command.application.dto.request.activity;

namespace AdhdTimeOrganizer.Command.application.dto.request.extendable;

public record WithIsDoneRequest(long ActivityId, bool IsDone = false) : ActivityIdRequest(ActivityId);