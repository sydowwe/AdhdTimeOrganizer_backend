using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.generic;

public record IdRequest(
    [Required] long Id
) : IMyRequest;