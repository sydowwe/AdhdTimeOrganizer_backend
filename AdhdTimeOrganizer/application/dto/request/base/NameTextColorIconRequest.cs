using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.application.dto.request.@base;

public record NameTextColorIconRequest : NameTextColorRequest
{
    [StringLength(255)]
    public string? Icon { get; init; }
}