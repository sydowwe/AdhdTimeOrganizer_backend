using AdhdTimeOrganizer.Core.application.dto.request.activity;
using Sydowwe.Framework.application.dto.request.@interface;
using Sydowwe.Framework.domain.valueObject;

namespace AdhdTimeOrganizer.History.application.dto.filter;

public record ActivityHistoryFilterRequest : ActivitySelectForm, IFilterRequest
{
    public DateTime? DateFrom { get; init; }
    public DateTime? DateTo { get; init; }
    public long? HoursBack { get; init; }

    public IntTime? MinLength { get; init; }

    public IntTime? MaxLength { get; init; }
}