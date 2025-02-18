using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Common.application.dto.request.@base;

namespace AdhdTimeOrganizer.Common.application.dto.request.generic;

public record IdRequest(
    [Required] long Id
) : IMyRequest;