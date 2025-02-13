using AdhdTimeOrganizer.Common.application.dto.request.@base;

namespace AdhdTimeOrganizer.Command.application.dto.request.activity;

public record ActivitySelectForm(
    long? ActivityId,
    long? RoleId,
    long? CategoryId,
    bool? IsFromToDoList,
    bool? IsUnavoidable
) : IMyRequest;