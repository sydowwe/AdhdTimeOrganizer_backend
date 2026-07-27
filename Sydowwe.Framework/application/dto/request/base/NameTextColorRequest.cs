using System.ComponentModel.DataAnnotations;

namespace Sydowwe.Framework.application.dto.request.@base;

public record NameTextColorRequest : NameTextRequest
{
    [Required]
    [StringLength(7)]
    public required string Color { get; init; }
}