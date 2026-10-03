using AdhdTimeOrganizer.History.application.dto.request.history;
using AdhdTimeOrganizer.History.domain.model.@enum;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.History.application.validator;

public class CreateActivityHistorySessionValidator : Validator<CreateActivityHistorySessionRequest>
{
    /// <summary>
    /// The most records one session may carry. A 4-period, 2-cycle Pomodoro is 15 phases; the cap only stops one
    /// request inserting an unbounded number of rows. Same figure as the timer-alarm boundary cap.
    /// </summary>
    public const int MaxRecords = ScheduleTimerAlarmsValidator.MaxBoundaries;

    public CreateActivityHistorySessionValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Outcome).IsInEnum();

        RuleFor(x => x.Outcome)
            .NotEqual(TimerSessionOutcome.RanOutWhileAway)
            .When(x => x.Kind == TimerSessionKind.Timer)
            .WithMessage("RanOutWhileAway is only valid for a Pomodoro — a countdown that ran out while away is Completed.");

        // ---- focus-period counts: both for a Pomodoro, neither for a Timer ----------------------------

        When(x => x.Kind == TimerSessionKind.Pomodoro, () =>
        {
            RuleFor(x => x.PlannedFocusPeriods)
                .NotNull().WithMessage("A Pomodoro session must say how many focus periods it planned.")
                .GreaterThan(0);

            RuleFor(x => x.CompletedFocusPeriods)
                .NotNull().WithMessage("A Pomodoro session must say how many focus periods it completed.")
                .GreaterThanOrEqualTo(0);

            RuleFor(x => x.CompletedFocusPeriods)
                .LessThanOrEqualTo(x => x.PlannedFocusPeriods!.Value)
                .When(x => x.PlannedFocusPeriods is not null && x.CompletedFocusPeriods is not null)
                .WithMessage("A session cannot complete more focus periods than it planned.");
        });

        When(x => x.Kind == TimerSessionKind.Timer, () =>
        {
            RuleFor(x => x.PlannedFocusPeriods).Null().WithMessage("A Timer session has no focus periods.");
            RuleFor(x => x.CompletedFocusPeriods).Null().WithMessage("A Timer session has no focus periods.");
        });

        RuleFor(x => x.PlannedLength)
            .Must(l => l!.TotalSeconds > 0)
            .When(x => x.PlannedLength is not null)
            .WithMessage("PlannedLength must be positive when sent.");

        // ---- the item link: ActivityHistoryValidator's rules, verbatim -------------------------------

        RuleFor(x => x.TodoListItemId)
            .GreaterThan(0)
            .When(x => x.TodoListItemId.HasValue);

        RuleFor(x => x.RoutineTodoListId)
            .GreaterThan(0)
            .When(x => x.RoutineTodoListId.HasValue);

        RuleFor(x => x)
            .Must(x => !(x.TodoListItemId.HasValue && x.RoutineTodoListId.HasValue))
            .WithMessage("A session cannot be linked to both a to-do item and a routine item.")
            .WithName(nameof(CreateActivityHistorySessionRequest.TodoListItemId));

        // ---- records ----------------------------------------------------------------------------------

        RuleFor(x => x.Records)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(r => r.Count > 0).WithMessage("A session must carry at least one record.")
            .Must(r => r.Count <= MaxRecords).WithMessage($"A session may carry at most {MaxRecords} records.");

        RuleFor(x => x.Records)
            .Must(r => r.Count == 1)
            .When(x => x.Kind == TimerSessionKind.Timer && x.Records is { Count: > 0 })
            .WithMessage("A Timer session is exactly one record.");

        // Start order is the contract, and strictly increasing is what keeps two records of one activity off
        // the (UserId, ActivityId, StartTimestamp) unique index — which would otherwise answer 409 for what is
        // a malformed body. Overlap *is* allowed: whole-minute rounding can push a phase's end up to 59 s past
        // the next phase's start.
        RuleFor(x => x.Records)
            .Must(BeInStrictStartOrder)
            .When(x => x.Records is { Count: > 1 } && x.Records.All(r => r is not null))
            .WithMessage("Records must be sent in start order, with no two starting at the same instant.");

        // ChildRules passes a null element as valid, and ToEntity would then throw — a 500 for a 400 body.
        RuleForEach(x => x.Records).NotNull();

        RuleForEach(x => x.Records).ChildRules(record =>
        {
            record.RuleFor(r => r.ActivityId).NotEmpty();

            // Stricter than the single-row create, deliberately: every instant here was computed from the
            // client's clock, and one read as server-local would slide the whole session by the server's
            // offset. Same rule as the timer alarms' BoundaryAt.
            record.RuleFor(r => r.StartTimestamp)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .Must(at => at.Kind != DateTimeKind.Unspecified)
                .WithMessage("StartTimestamp must be an ISO-8601 instant carrying an offset (a trailing 'Z' is the usual one).");

            // B17's rule, per record — see ActivityHistoryValidator for why Cascade(Stop) is load-bearing.
            record.RuleFor(r => r.Length)
                .Cascade(CascadeMode.Stop)
                .NotNull()
                .Must(l => l.TotalSeconds > 0)
                .WithMessage("Length must be positive.");
        });

        RuleForEach(x => x.Records).ChildRules(record =>
            record.RuleFor(r => r.Phase)
                .NotNull()
                .WithMessage("Every Pomodoro record must say which phase it is.")
                .IsInEnum())
            .When(x => x.Kind == TimerSessionKind.Pomodoro);

        RuleForEach(x => x.Records).ChildRules(record =>
            record.RuleFor(r => r.Phase)
                .Null()
                .WithMessage("A Timer record has no phase."))
            .When(x => x.Kind == TimerSessionKind.Timer);
    }

    private static bool BeInStrictStartOrder(List<ActivityHistorySessionRecordRequest> records)
    {
        for (var i = 1; i < records.Count; i++)
            if (records[i].StartTimestamp.ToUniversalTime() <= records[i - 1].StartTimestamp.ToUniversalTime())
                return false;
        return true;
    }
}
