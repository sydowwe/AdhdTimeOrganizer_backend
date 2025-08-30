using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.application.dto.request.generic;

public record SortByRequest
{
    [Required] public required string Key { get; init; }
    [Required] public required bool IsDesc { get; init; }
}