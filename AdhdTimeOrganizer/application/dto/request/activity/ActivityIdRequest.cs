using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.extendable;

namespace AdhdTimeOrganizer.application.dto.request.activity;


public record ActivityIdRequest : IActivityIdRequest
{
    [Required]
    public long ActivityId { get; init; }
}