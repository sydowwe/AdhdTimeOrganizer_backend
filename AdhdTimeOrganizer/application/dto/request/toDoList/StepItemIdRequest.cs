using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.toDoList;

public record StepItemIdRequest(
    [Required] long ItemId
) : IMyRequest;
