using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.todoList;

public record StepItemAndStepIdRequest(
    [Required] long ItemId,
    [Required] Guid StepId
);
