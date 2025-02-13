using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Command.application.dto.request.activity;
using AdhdTimeOrganizer.Command.application.dto.request.extendable;

namespace AdhdTimeOrganizer.Command.application.dto.request;

public record AlarmRequest(
    string Name,
    string? Text,
    string Color,
    [ Required] DateTime StartTimestamp,
    [ Required] long ActivityId,
    [ Required] bool IsActive
) : NameTextColorRequest(Name, Text, Color), IActivityIdRequest;