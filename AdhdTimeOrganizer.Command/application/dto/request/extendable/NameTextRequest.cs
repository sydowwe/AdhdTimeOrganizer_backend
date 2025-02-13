using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Common.application.dto.request.@base;

namespace AdhdTimeOrganizer.Command.application.dto.request.extendable;

public record NameTextRequest(
    [ Required, StringLength(50)] string Name,
    [ StringLength(500)] string? Text
) : IMyRequest;