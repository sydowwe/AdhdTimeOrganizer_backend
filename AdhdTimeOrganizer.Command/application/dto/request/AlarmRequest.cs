using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Command.application.dto.request.activity;
using AdhdTimeOrganizer.Command.application.dto.request.extendable;
using AdhdTimeOrganizer.Common.application.dto.request.@base;

namespace AdhdTimeOrganizer.Command.application.dto.request;

public record AlarmRequest : NameTextColorRequest, IActivityIdRequest
{
    [Required]
    public DateTime StartTimestamp { get; init; }

    [Required]
    public long ActivityId { get; init; }

    [Required]
    public bool IsActive { get; init; }
}