using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.timer;

public record TimerPresetRequest : IMyRequest
{
    [Required]
    public required int Duration { get; init; }

    public long? ActivityId { get; init; }
}
