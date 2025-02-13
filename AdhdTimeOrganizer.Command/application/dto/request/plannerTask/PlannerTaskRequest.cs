using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Command.application.dto.request.extendable;

namespace AdhdTimeOrganizer.Command.application.dto.request.plannerTask;

public record PlannerTaskRequest(
    long ActivityId,
    bool IsDone,
    [ Required] DateTime StartTimestamp,
    [ Range(1, 720)] int MinuteLength
) : WithIsDoneRequest(ActivityId, IsDone);