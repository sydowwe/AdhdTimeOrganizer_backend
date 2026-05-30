using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.extendable;
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.activity;


public record ActivityIdRequest : IActivityIdRequest
{
    [Required]
    public long ActivityId { get; init; }
}