using AdhdTimeOrganizer.application.dto.request.user;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class ApplyFreshStartValidator : Validator<ApplyFreshStartRequest>
{
    /// <summary>Two weeks. Past that, a spread is a backlog with dates on it, which is what "someday" is for.</summary>
    public const int MaxSpreadDays = 14;

    public ApplyFreshStartValidator()
    {
        RuleFor(x => x.Todos).IsInEnum();
        RuleFor(x => x.PastTasks).IsInEnum();

        RuleFor(x => x.SpreadOverDays)
            .InclusiveBetween(1, MaxSpreadDays)
            .When(x => x.Todos == FreshStartTodoChoice.Spread);
    }
}

public class UndoFreshStartValidator : Validator<UndoFreshStartRequest>
{
    public UndoFreshStartValidator()
    {
        RuleFor(x => x.UndoToken).NotEmpty();
    }
}
