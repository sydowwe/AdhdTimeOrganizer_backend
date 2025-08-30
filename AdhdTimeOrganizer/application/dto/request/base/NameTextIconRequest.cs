using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.application.dto.request.@base;

public record NameTextIconRequest : NameTextRequest
{
    [StringLength(255)] public string? Icon { get; init; }
}