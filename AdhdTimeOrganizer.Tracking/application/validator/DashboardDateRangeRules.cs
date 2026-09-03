using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using FluentValidation;

namespace AdhdTimeOrganizer.Tracking.application.validator;

/// <summary>
/// The span rules every tracking dashboard validator shares. Kept in one place because the twelve
/// dashboards already duplicate enough: a span bound applied to eleven of them and forgotten on the
/// twelfth is an unbounded read of a partitioned ledger, and nothing about it looks wrong.
/// </summary>
public static class DashboardDateRangeRules
{
    /// <summary>
    /// A year and a day. Long enough for "this year" on every dashboard, short enough that no single
    /// request walks the whole retention window of a ledger.
    /// </summary>
    public const int MaxSpanDays = 366;

    /// <summary>
    /// Bucket widths the stacked-bars dashboards accept. The single-day set (15–120) plus the widths the
    /// client picks adaptively over a span from its column budget: 240 and 480 sub-daily, then whole-day
    /// multiples for a day, three days and a week. Nothing lands between 480 and 1440 because the tiling
    /// rule changes there — under a day buckets tile each day's window, at a day and over they tile the
    /// span.
    /// </summary>
    public static readonly int[] AllowedWindowMinutes = [15, 20, 30, 60, 90, 120, 240, 480, 1440, 4320, 10080];

    public const string WindowMinutesMessage =
        "WindowMinutes must be 15, 20, 30, 60, 90, 120, 240, 480, 1440, 4320 or 10080";

    public static void ApplyDateRangeRules<T>(this AbstractValidator<T> validator)
        where T : DateRangeAndTimeRangeDto
    {
        validator.RuleFor(x => x.DateFrom).NotEmpty();
        validator.RuleFor(x => x.DateTo).NotEmpty();
        // Range-checked, not merely non-empty: ToDailyWindows resolves both through TimeDto.ToTimeOnly(),
        // which throws on an out-of-range value and turns what should be a 400 into a 500.
        validator.RuleFor(x => x.From).NotEmpty().ApplyTimeOfDayRules();
        validator.RuleFor(x => x.To).NotEmpty().ApplyTimeOfDayRules();

        validator.RuleFor(x => x.DateTo)
            .GreaterThanOrEqualTo(x => x.DateFrom)
            .WithMessage("DateTo must be on or after DateFrom");

        validator.RuleFor(x => x.DayCount)
            .LessThanOrEqualTo(MaxSpanDays)
            .WithMessage($"Maximum range is {MaxSpanDays} days");
    }

    /// <summary>
    /// The span the two details endpoints accept. Narrower than <see cref="MaxSpanDays"/> because a
    /// details panel is a drill-down into one dashboard slice, and its read is filtered by a single
    /// process or domain — a predicate no index supports, so the span is the only thing bounding it.
    /// </summary>
    public const int MaxDetailsSpanDays = 31;

    /// <summary>
    /// The envelope rules both details endpoints share. Hoisted out of the two validators because the
    /// span cap was on one of them and not the other (<c>PERF-3</c>), which is exactly the drift this
    /// class exists to stop.
    /// </summary>
    public static void ApplyDetailsDateRangeRules<T>(this AbstractValidator<T> validator)
        where T : DailyWindowMaskRequest
    {
        validator.RuleFor(x => x.From).NotEmpty();
        validator.RuleFor(x => x.To).NotEmpty().GreaterThan(x => x.From);
        validator.RuleFor(x => x.To)
            .LessThanOrEqualTo(x => x.From.AddDays(MaxDetailsSpanDays))
            .WithMessage($"Maximum range is {MaxDetailsSpanDays} days");
        validator.ApplyDailyWindowMaskRules();
    }

    /// <summary>
    /// The details endpoints only. The per-day window is optional but not half-optional: one bound
    /// without the other has no reading, and silently ignoring it would put the panel back to
    /// over-counting with no sign that anything was dropped.
    /// </summary>
    public static void ApplyDailyWindowMaskRules<T>(this AbstractValidator<T> validator)
        where T : DailyWindowMaskRequest
    {
        validator.RuleFor(x => x.WindowStartMinutes)
            .InclusiveBetween(0, 1439)
            .When(x => x.WindowStartMinutes.HasValue);

        validator.RuleFor(x => x.WindowEndMinutes)
            .InclusiveBetween(0, 1439)
            .When(x => x.WindowEndMinutes.HasValue);

        validator.RuleFor(x => x.WindowEndMinutes)
            .NotNull()
            .When(x => x.WindowStartMinutes.HasValue)
            .WithMessage("WindowStartMinutes and WindowEndMinutes must be sent together");

        validator.RuleFor(x => x.WindowStartMinutes)
            .NotNull()
            .When(x => x.WindowEndMinutes.HasValue)
            .WithMessage("WindowStartMinutes and WindowEndMinutes must be sent together");
    }

    /// <summary>
    /// The timeline dashboards only. A range is rejected outright rather than answered with something
    /// unreadable — see <see cref="BaseTimelineRequest"/>.
    /// </summary>
    public static void ApplySingleDayRule<T>(this AbstractValidator<T> validator)
        where T : DateRangeAndTimeRangeDto
    {
        validator.RuleFor(x => x.DateTo)
            .Equal(x => x.DateFrom)
            .WithMessage("The timeline is a single-day dashboard: DateTo must equal DateFrom");
    }
}
