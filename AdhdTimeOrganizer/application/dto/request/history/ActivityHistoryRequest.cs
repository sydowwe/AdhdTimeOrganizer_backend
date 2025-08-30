using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.activity;
using AdhdTimeOrganizer.domain.helper;


namespace AdhdTimeOrganizer.application.dto.request.history;
public record ActivityHistoryRequest : ActivityIdRequest
{

    [Required]
    public required DateTime StartTimestamp { get; init; }

    [Required]
    public required MyIntTime Length { get; init; }
}