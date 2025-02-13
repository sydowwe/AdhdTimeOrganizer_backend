using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Common.application.dto.request.@base;

namespace AdhdTimeOrganizer.Command.application.dto.request.extendable;


public record TextColorRequest(
    [ Required, StringLength(500)] string Text,
    [ Required, StringLength(7)] string Color
) : IMyRequest;