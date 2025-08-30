using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.activity;

namespace AdhdTimeOrganizer.application.dto.request;

public record WebExtensionDataRequest : ActivityIdRequest
{
    [Required, StringLength(255)]
    public required string Domain { get; init; }

    [Required, StringLength(255)]
    public required string Title { get; init; }

    [Required]
    public required int Duration { get; init; }

    [Required]
    public required DateTime StartTimestamp { get; init; }
}