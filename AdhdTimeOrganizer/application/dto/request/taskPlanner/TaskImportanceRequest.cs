using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@base;

namespace AdhdTimeOrganizer.application.dto.request.toDoList;

public record TaskImportanceRequest : TextColorRequest
{
    [Required]
    public short Importance { get; init; }
}
