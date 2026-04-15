using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.toDoList;

public record StepItemAndStepIdRequest(
    [Required] long ItemId,
    [Required] Guid StepId
) : IMyRequest;
