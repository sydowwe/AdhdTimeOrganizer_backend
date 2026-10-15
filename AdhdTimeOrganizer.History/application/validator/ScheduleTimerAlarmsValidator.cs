using AdhdTimeOrganizer.History.application.dto.request.timerAlarm;
using AdhdTimeOrganizer.History.domain.model.@enum;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.History.application.validator;

public class ScheduleTimerAlarmsValidator : Validator<ScheduleTimerAlarmsRequest>
{
    /// <summary>
    /// The largest preset worth scheduling in one go. A 4-period, 2-cycle pomodoro is 15 boundaries, so 64 is
    /// far past any real timer; the cap is what stops one request from creating an unbounded number of live
    /// Quartz triggers — the one resource this design spends that the Reminders module deliberately does not.
    /// </summary>
    public const int MaxBoundaries = 64;

    /// <summary>
    /// How far ahead a boundary may be scheduled. A timer runs for minutes; a day is generous and bounds how
    /// long a row can sit in a table that has no retention job because it is supposed to self-drain.
    /// </summary>
    public static readonly TimeSpan MaxHorizon = TimeSpan.FromDays(1);

    public ScheduleTimerAlarmsValidator()
    {
        RuleFor(x => x.SessionId)
            .NotEmpty()
            .WithMessage("A session id is required — it is the handle every later cancel uses.");

        RuleFor(x => x.Boundaries)
            .NotNull()
            .Must(b => b is { Count: > 0 })
            .WithMessage("At least one boundary is required.")
            .Must(b => b is null || b.Count <= MaxBoundaries)
            .WithMessage($"At most {MaxBoundaries} boundaries may be scheduled per session.");

        RuleFor(x => x.ActivityId)
            .GreaterThan(0)
            .When(x => x.ActivityId is not null);

        // Relative only. An absolute URL reaches the service worker verbatim and would turn a push the user
        // trusts into an off-site redirect.
        RuleFor(x => x.Url)
            .Must(url => url is null || (url.StartsWith('/') && !url.StartsWith("//")))
            .WithMessage("Url must be a relative app route beginning with a single '/'.")
            .MaximumLength(512);

        RuleForEach(x => x.Boundaries).ChildRules(boundary =>
        {
            boundary.RuleFor(b => b.BoundaryAt)
                .Cascade(CascadeMode.Stop)
                // An instant with no offset is read as *server local* time by ToUniversalTime, so an
                // unsuffixed value was validated against one interpretation and then stored under another
                // (the service labels it UTC). Both readings are wrong on a non-UTC server, and the
                // difference is silent: the alarm simply rings at the wrong hour. Require the offset.
                .Must(at => at.Kind != DateTimeKind.Unspecified)
                .WithMessage("BoundaryAt must be an ISO-8601 instant carrying an offset (a trailing 'Z' is the usual one).")
                .Must(at => at.ToUniversalTime() <= DateTime.UtcNow + MaxHorizon)
                .WithMessage($"A boundary may be at most {MaxHorizon.TotalHours:0} hours ahead.");

            // Past boundaries are accepted and silently dropped by the service rather than rejected: a set
            // re-sent after a slow reload legitimately contains boundaries that have since elapsed, and
            // failing the whole request would cost the user the alarms that are still to come.
            boundary.RuleFor(b => b.Phase).IsInEnum().When(b => b.Phase is not null);
            boundary.RuleFor(b => b.PhaseIndex).GreaterThan(0).When(b => b.PhaseIndex is not null);
            boundary.RuleFor(b => b.PhaseTotal).GreaterThan(0).When(b => b.PhaseTotal is not null);
            boundary.RuleFor(b => b.CycleIndex).GreaterThan(0).When(b => b.CycleIndex is not null);
            boundary.RuleFor(b => b.CycleTotal).GreaterThan(0).When(b => b.CycleTotal is not null);
        });

        RuleFor(x => x.Kind).IsInEnum();

        // Which fields a boundary carries depends on the session's kind, which the child rules above cannot see.
        // A phase boundary is worded from its phase; a check-in from its running time, and a phase on it would be
        // a claim about a cycle a stopwatch does not have — refused rather than silently ignored.
        RuleForEach(x => x.Boundaries)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(b => b.Phase is not null)
            .WithMessage("A timer boundary needs a phase.")
            .Must(b => b.ElapsedMinutes is null)
            .WithMessage("elapsedMinutes belongs to a stopwatch check-in, not a timer boundary.")
            .When(x => x.Kind == TimerAlarmKind.Boundary);

        RuleForEach(x => x.Boundaries)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(b => b.ElapsedMinutes is > 0)
            .WithMessage("A stopwatch check-in needs a positive elapsedMinutes.")
            .Must(b => b is { Phase: null, PhaseIndex: null, PhaseTotal: null, CycleIndex: null, CycleTotal: null })
            .WithMessage("A stopwatch check-in carries no phase or cycle fields.")
            .When(x => x.Kind == TimerAlarmKind.StopwatchCheckIn);
    }
}
