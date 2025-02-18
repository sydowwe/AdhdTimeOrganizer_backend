using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Command.application.dto.request.activity;
using AdhdTimeOrganizer.Common.domain.helper;

namespace AdhdTimeOrganizer.Command.application.dto.request.history;
public record ActivityHistoryRequest : ActivityIdRequest
{

    [Required]
    public required DateTime StartTimestamp { get; init; }

    [Required]
    public required MyIntTime Length { get; init; }
}