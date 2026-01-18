using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.application.dto.request.@base;

public record NameTextRequest : UserIdRequest
{
    [Required, StringLength(50)] public required string Name { get; init; }

    [StringLength(500)] public string? Text { get; init; }
}