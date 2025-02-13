using AdhdTimeOrganizer.Command.application.dto.request.activity;

namespace AdhdTimeOrganizer.Command.application.dto.request.history;

public record ActivityHistoryFilterRequest(
    DateTime? DateFrom,
    DateTime? DateTo,
    long? HoursBack,


    long? ActivityId,
    long? RoleId,
    long? CategoryId,
    bool? IsFromToDoList,
    bool? IsUnavoidable
) : ActivitySelectForm(ActivityId, RoleId, CategoryId, IsFromToDoList, IsUnavoidable);