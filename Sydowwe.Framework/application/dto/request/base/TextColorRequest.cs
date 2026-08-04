using System.ComponentModel.DataAnnotations;

namespace Sydowwe.Framework.application.dto.request.@base;

public record TextColorRequest
{
    [Required]
    [StringLength(500)]
    public required string Text { get; init; }

    [Required]
    [StringLength(7)]
    public required string Color { get; init; }
}