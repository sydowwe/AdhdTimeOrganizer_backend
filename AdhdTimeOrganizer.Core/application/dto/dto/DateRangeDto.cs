using AdhdTimeOrganizer.Core.application.dto.@enum;
using FluentValidation;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Core.application.dto.dto;

public record DateRangeDto
{
    /// <summary>
    /// The longest span a <see cref="ActivityDateRangeType.CustomRange"/> may cover, counted inclusively —
    /// a full calendar year, leap day included.
    ///
    /// <para>The cap is on the <b>request</b>, not on what the query returns: every dashboard reading this
    /// DTO pulls the period's rows into memory and aggregates there (<c>summary-cards</c> runs three such
    /// reads per request, for the period, its baseline and the preceding period), so an uncapped custom
    /// range is an uncapped read. The named range types cannot express one — <c>Year</c> is the longest,
    /// and it is this same span give or take a leap day — so this only closes the hole custom ranges
    /// opened rather than restricting anything a user could already ask for.</para>
    ///
    /// <para>Deliberately a 400 rather than a silent clamp. Clamping is what this whole area was just fixed
    /// for: answering a different range than the one the header names, in a body the client cannot tell
    /// from the truth.</para>
    /// </summary>
    public const int MaxCustomRangeDays = 366;

    public required DateOnly Date { get; init; }
    public required ActivityDateRangeType RangeType { get; init; }

    /// <summary>
    /// The last day of the range, inclusive — read by <see cref="ActivityDateRangeType.CustomRange"/> and by
    /// nothing else. Every other <see cref="RangeType"/> derives both ends from <see cref="Date"/> and
    /// ignores this field, so a client that leaves a stale value on it while switching to a named range gets
    /// the named range, not an error.
    /// </summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>
    /// The range as a pair of the user's calendar days, <b>both ends inclusive</b> —
    /// <see cref="ToUtcRange"/> is what turns that into a half-open instant range by pushing <c>To</c> to the
    /// following midnight.
    ///
    /// <para><see cref="ActivityDateRangeType.CustomRange"/> is the one member that reads
    /// <see cref="EndDate"/>; a missing one collapses to the single day <see cref="Date"/> rather than
    /// inverting the range. Requests never arrive here in that state — <see cref="ApplyValidationRules{T}"/>
    /// rejects a <c>CustomRange</c> without an <see cref="EndDate"/>, and one that precedes
    /// <see cref="Date"/>, with a 400 — so the fallback exists only so a non-endpoint caller cannot produce
    /// <c>To &lt; From</c>.</para>
    ///
    /// <para>There is deliberately no "widen a one-day range to two days" step here. The previous one fired
    /// only on the fall-through arm, which is to say only on a custom range, and it was what silently turned
    /// <i>1–31 March</i> into <i>1–2 March</i> — a well-formed answer to a range nobody asked for. A range
    /// whose ends are the same day is one day, and every consumer walks it correctly
    /// (<c>day &lt;= toDate</c>, <c>DayNumber</c> difference plus one).</para>
    /// </summary>
    public (DateOnly From, DateOnly To) ToDateRange() =>
        RangeType switch
        {
            ActivityDateRangeType.ThreeDays => (Date.AddDays(-3), Date),
            ActivityDateRangeType.Week => (Date.AddDays(-7), Date),
            ActivityDateRangeType.TwoWeeks => (Date.AddDays(-14), Date),
            ActivityDateRangeType.Month => (Date.AddMonths(-1), Date),
            ActivityDateRangeType.ThreeMonths => (Date.AddMonths(-3), Date),
            ActivityDateRangeType.Year => (Date.AddYears(-1), Date),
            ActivityDateRangeType.CustomRange => (Date, EndDate is { } endDate && endDate >= Date ? endDate : Date),
            _ => (Date, Date)
        };

    /// <summary>
    /// <see cref="ToDateRange"/> resolved to the half-open UTC instant range the days actually cover on the
    /// clocks of <paramref name="timeZone"/> — the requesting user's own zone (<c>User.Timezone</c>).
    ///
    /// <para>Days are the user's days. Midnight in Bratislava is 23:00 the previous day in UTC, so anchoring
    /// the range at UTC midnight (which is what every caller of <see cref="ToDateRange"/> used to do) credits
    /// the user's last hour of every evening to the following day — the same class of error
    /// <c>PlannerStreakReader</c> already avoids by resolving <c>today</c> in the user's zone.</para>
    ///
    /// <para>The upper bound is exclusive and covers the whole of <c>To</c>: the range ends at the user's
    /// midnight opening the day <i>after</i> it.</para>
    /// </summary>
    public (DateTime From, DateTime To) ToUtcRange(TimeZoneInfo timeZone)
    {
        var (fromDate, toDate) = ToDateRange();

        return (
            WallClockZone.ToUtc(fromDate, TimeOnly.MinValue, timeZone),
            WallClockZone.ToUtc(toDate.AddDays(1), TimeOnly.MinValue, timeZone));
    }

    /// <summary>
    /// The rules every range-taking request needs. Attach them from a concrete
    /// <c>FastEndpoints.Validator&lt;TRequest&gt;</c> — FastEndpoints resolves validators by exact request
    /// type, so an abstract or open-generic base is never discovered on its own and a request type without
    /// its own concrete validator is silently unvalidated. That is what these rules were until now: they
    /// were written and never called from anywhere.
    ///
    /// <para><see cref="EndDate"/> is only constrained on
    /// <see cref="ActivityDateRangeType.CustomRange"/> — present, not before <see cref="Date"/>, and within
    /// <see cref="MaxCustomRangeDays"/> of it. On every other member it is ignored rather than rejected, so
    /// a client that carries the field around in a URL and switches the range type does not start getting
    /// 400s for a value the server was never going to read.</para>
    /// </summary>
    public static void ApplyValidationRules<T>(AbstractValidator<T> validator, Func<T, DateRangeDto> selector)
    {
        // Every rule here names its own property. The selector makes each RuleFor a lambda *invocation*
        // rather than a member access, and FluentValidation cannot infer a name from that — it produces an
        // empty one, which FastEndpoints surfaces as errors keyed on "" and leaves the client unable to put
        // the message next to the field it is about. The names are the wire names.
        validator.RuleFor(x => selector(x).Date)
            .NotEmpty()
            .OverridePropertyName("date");

        validator.RuleFor(x => selector(x).RangeType)
            .IsInEnum()
            .OverridePropertyName("rangeType");

        validator.RuleFor(x => selector(x).EndDate)
            .NotNull()
            .WithMessage("EndDate is required when RangeType is CustomRange")
            .Must((root, endDate) => endDate is null || endDate >= selector(root).Date)
            .WithMessage("EndDate must be greater than or equal to Date")
            // Inclusive day count, so the two ends may be MaxCustomRangeDays - 1 apart. Ordered after the
            // rule above and short-circuited on it: an inverted range is already reported as inverted, and
            // a negative span reported as "too long" would send the client looking in the wrong place.
            .Must((root, endDate) => endDate is null
                                     || endDate < selector(root).Date
                                     || endDate.Value.DayNumber - selector(root).Date.DayNumber
                                     < MaxCustomRangeDays)
            .WithMessage($"A custom range may span at most {MaxCustomRangeDays} days")
            .OverridePropertyName("endDate")
            // Applies to every component in the chain above, not just the last one — pinned by
            // HistorySummaryCustomRangeTests.NamedRangeTypes_IgnoreEndDate_RatherThanReadingOrRejectingIt,
            // which would fail on a stale endDate if this narrowed to the span rule alone.
            .When(x => selector(x).RangeType == ActivityDateRangeType.CustomRange);
    }
}