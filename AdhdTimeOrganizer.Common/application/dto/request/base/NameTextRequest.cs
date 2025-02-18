using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.Common.application.dto.request.@base;

public record NameTextRequest : IMyRequest
{
    [Required, StringLength(50)]
    public required string Name { get; init; }

    [StringLength(500)]
    public string? Text { get; init; }
}