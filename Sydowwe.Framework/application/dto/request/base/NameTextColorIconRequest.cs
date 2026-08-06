using System.ComponentModel.DataAnnotations;

namespace Sydowwe.Framework.application.dto.request.@base;

public record NameTextColorIconRequest : NameTextColorRequest
{
    [StringLength(255)]
    public string? Icon { get; init; }
}