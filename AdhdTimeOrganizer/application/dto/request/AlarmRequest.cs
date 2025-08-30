using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@base;
using AdhdTimeOrganizer.application.dto.request.extendable;

namespace AdhdTimeOrganizer.application.dto.request;

public record AlarmRequest : NameTextColorRequest, IActivityIdRequest
{
    [Required]
    public DateTime StartTimestamp { get; init; }

    [Required]
    public long ActivityId { get; init; }

    [Required]
    public bool IsActive { get; init; }
}