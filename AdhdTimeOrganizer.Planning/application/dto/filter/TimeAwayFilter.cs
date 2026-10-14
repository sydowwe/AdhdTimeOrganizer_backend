using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.Planning.application.dto.filter;

/// <summary>
/// <c>POST /time-away/filter</c>: the stretches that touch <c>[From, Until]</c>, both ends inclusive. Either end may
/// be left out — no <c>Until</c> is "from this date on", no <c>From</c> is "up to this date", and neither is every
/// stretch the user has, which is what the <i>Time away</i> list reads.
/// </summary>
public record TimeAwayFilter : IFilterRequest
{
    public DateOnly? From { get; init; }

    public DateOnly? Until { get; init; }
}
