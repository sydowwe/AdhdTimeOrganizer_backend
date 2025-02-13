using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Common.application.dto.request.@base;

namespace AdhdTimeOrganizer.Common.application.dto.request.generic;

public class IdRequest : IMyRequest
{
    [Required] public long Id { get; set; }
}