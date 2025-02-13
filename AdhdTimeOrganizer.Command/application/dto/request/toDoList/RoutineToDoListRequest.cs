using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Command.application.dto.request.extendable;

namespace AdhdTimeOrganizer.Command.application.dto.request.toDoList;

public record RoutineToDoListRequest(
    long ActivityId,
    bool IsDone,
    [ Required] long TimePeriodId
) : WithIsDoneRequest(ActivityId, IsDone);