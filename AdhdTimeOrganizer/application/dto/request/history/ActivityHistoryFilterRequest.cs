using AdhdTimeOrganizer.application.dto.request.activity;

namespace AdhdTimeOrganizer.application.dto.request.history;

public record ActivityHistoryFilterRequest : ActivitySelectForm
{
    public DateTime? DateFrom { get; init; }
    public DateTime? DateTo { get; init; }
    public long? HoursBack { get; init; }
}