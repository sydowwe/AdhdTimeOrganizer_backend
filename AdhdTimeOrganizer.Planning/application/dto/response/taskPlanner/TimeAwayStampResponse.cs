using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Planning.application.helper;

namespace AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;

/// <summary>
/// The answer to a create or an update of a stretch of time away: its id, and what the stamp did to each day — the
/// per-day report the batched calendar writes give, so the client can say "2 days kept their own type".
/// <para>
/// <b>Unlike those batches, there is no per-day failure.</b> The stretch and its stamp are one transaction; a day
/// either took the stretch's kind or kept a type the user had set (<see cref="TimeAwayDayResponse.KeptOwnDayType"/>),
/// and either way it belongs to the stretch.
/// </para>
/// </summary>
public record TimeAwayStampResponse
{
    public required long Id { get; init; }

    /// <summary>Every day of the stretch, in date order — including the ones this request created.</summary>
    public required List<TimeAwayDayResponse> Days { get; init; }

    /// <summary>
    /// Days the stretch held before this request and no longer covers, now back to their own type. Always empty on
    /// a create.
    /// </summary>
    public required List<DateOnly> ReleasedDates { get; init; }

    public static TimeAwayStampResponse From(long id, TimeAwayStaging staging) => new()
    {
        Id = id,
        Days = staging.Days
            .Select(d => new TimeAwayDayResponse
            {
                Date = d.Day.Date,
                CalendarId = d.Day.Id,
                DayType = d.Day.DayType,
                KeptOwnDayType = !d.TookKind
            })
            .ToList(),
        ReleasedDates = staging.ReleasedDates.ToList()
    };
}

public record TimeAwayDayResponse
{
    public required DateOnly Date { get; init; }

    /// <summary>The day's <c>Calendar.Id</c> — the same id <c>calendar/filter</c> returns.</summary>
    public required long CalendarId { get; init; }

    /// <summary>The day's type now: the stretch's kind, or the user's own one.</summary>
    public required DayType DayType { get; init; }

    /// <summary>
    /// The day already had a type of the user's own (a <c>Special</c> day, a sick day inside a vacation) and kept
    /// it. It still belongs to the stretch.
    /// </summary>
    public required bool KeptOwnDayType { get; init; }
}
