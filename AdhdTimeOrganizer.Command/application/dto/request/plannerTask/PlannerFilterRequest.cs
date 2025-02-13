using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Common.application.dto.request.@base;

namespace AdhdTimeOrganizer.Command.application.dto.request.plannerTask;

public record PlannerFilterRequest(
    [ Required] DateTime FilterDate,
    [ Range(1, 72)] int HourSpan
): IMyRequest;