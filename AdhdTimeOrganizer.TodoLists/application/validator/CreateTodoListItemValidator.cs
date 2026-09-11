using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.TodoLists.application.validator;

public class CreateTodoListItemValidator : Validator<CreateTodoListItemRequest>
{
    public CreateTodoListItemValidator()
    {
        RuleFor(x => x.TaskPriorityId)
            .GreaterThan(0L);

        RuleFor(x => x.TodoListId)
            .GreaterThan(0L);

        RuleFor(x => x.TotalCount)
            .InclusiveBetween(2, 99)
            .When(x => x.TotalCount.HasValue);

        RuleFor(x => x.PairedLeisureActivityId)
            .GreaterThan(0L)
            .When(x => x.PairedLeisureActivityId.HasValue);

        // Bundling a task with its own activity is meaningless — the reward has to be a different
        // activity than the work. That the id exists and belongs to the caller is not checked here:
        // the FK covers existence, and ownership needs the principal, so it lives in the endpoint's
        // BeforeMapping alongside the other three ids. This slice also cannot see ActivityProfiles to
        // ask whether the activity is a leisure one — offering only backlog entries is the picker's job.
        RuleFor(x => x.PairedLeisureActivityId)
            .NotEqual(x => (long?)x.ActivityId)
            .When(x => x.PairedLeisureActivityId.HasValue)
            .WithMessage("A task cannot be paired with its own activity.");

        RuleFor(x => x.Note)
            .MaximumLength(1000)
            .When(x => x.Note != null);

        RuleFor(x => x.SuggestedTime)
            .Must(t => t!.Hours is >= 0 and <= 23 && t.Minutes is >= 0 and <= 59)
            .When(x => x.SuggestedTime != null)
            .WithMessage("SuggestedTime hours must be 0–23 and minutes 0–59.");

        // Load-bearing, not defensive: DueTime became a TimeDto on 2026-09-11, and the mapping calls
        // TimeDto.ToTimeOnly() — which is new TimeOnly(Hours, Minutes) and *throws* out of range. Without
        // this rule an hours: 99 request is an unhandled 500 instead of a 400. A Validator<TimeDto> would
        // not cover it: FastEndpoints resolves validators by the endpoint's exact request type, so one
        // declared over a member DTO never runs. See TimeDtoRules and TimeDtoRangeValidationTests.
        RuleFor(x => x.DueTime!)
            .ApplyTimeOfDayRules()
            .When(x => x.DueTime != null);

        RuleForEach(x => x.Steps)
            .ChildRules(step =>
            {
                step.RuleFor(s => s.Name).MaximumLength(255);
                step.RuleFor(s => s.Note).MaximumLength(1000).When(s => s.Note != null);
            })
            .When(x => x.Steps != null);
    }
}