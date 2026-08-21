namespace AdhdTimeOrganizer.Core.application.dto.@enum;

public enum ActivityDateRangeType
{
    ThreeDays,
    Week,
    TwoWeeks,
    Month,
    ThreeMonths,
    Year,

    /// <summary>
    /// The only member that reads <c>DateRangeDto.EndDate</c>: the range is <c>Date</c> through
    /// <c>EndDate</c> inclusive, both being the user's own calendar days. Appended last on purpose — the
    /// wire format is the member name (<c>JsonStringEnumConverter</c>, no naming policy), but the numeric
    /// values are what a persisted or logged value would carry, so the six existing members keep theirs.
    /// </summary>
    CustomRange
}
