using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Command.application.dto.request.extendable;
using AdhdTimeOrganizer.Common.application.dto.request.@base;

namespace AdhdTimeOrganizer.Command.application.dto.request.activity;


public record ActivityIdRequest : IActivityIdRequest
{
    [Required]
    public long ActivityId { get; init; }
}