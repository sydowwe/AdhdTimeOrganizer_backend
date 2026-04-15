using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.generic;

public record ToggleIsDoneRequest(
    [Required] List<long> Ids,
    bool? ForceValue = null
) : IMyRequest;
