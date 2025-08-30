using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.@base;


public record TextColorRequest : IMyRequest
{
    [Required, StringLength(500)]
    public required string Text { get; init; }

    [Required, StringLength(7)]
    public required string Color { get; init; }
}