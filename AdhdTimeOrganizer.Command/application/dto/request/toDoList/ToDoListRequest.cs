using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Command.application.dto.request.extendable;

namespace AdhdTimeOrganizer.Command.application.dto.request.toDoList;

public record ToDoListRequest(
    long ActivityId,
    bool IsDone,
    [ Required] long UrgencyId
) : WithIsDoneRequest(ActivityId, IsDone);