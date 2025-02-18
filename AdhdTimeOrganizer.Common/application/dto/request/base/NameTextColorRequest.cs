using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.Common.application.dto.request.@base;

public record NameTextColorRequest : NameTextRequest
{
    [Required, StringLength(7)]
    public required string Color { get; init; }
}