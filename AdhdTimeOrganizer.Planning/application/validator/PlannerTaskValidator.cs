using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Planning.application.validator;

public class PlannerTaskValidator : Validator<PlannerTaskRequest>
{
    public PlannerTaskValidator()
    {
        RuleFor(x => x.ActivityId)
            .GreaterThan(0);

        RuleFor(x => x.ImportanceId)
            .GreaterThan(0)
            .When(x => x.ImportanceId.HasValue);

        // A task's day is named either by calendar id or by date, never both and never neither. The date form
        // is what makes an unseeded day plannable — it creates the calendar row — so a request carrying both
        // is rejected rather than silently preferring one: the two could disagree, and the loser would be a
        // task quietly filed on a day the caller did not ask for.
        RuleFor(x => x)
            .Must(x => x.CalendarId.HasValue ^ x.Date.HasValue)
            .WithMessage("Provide exactly one of CalendarId or Date.");

        RuleFor(x => x.CalendarId)
            .GreaterThan(0)
            .When(x => x.CalendarId.HasValue);

        RuleFor(x => x.TodolistId)
            .GreaterThan(0)
            .When(x => x.TodolistId.HasValue);

        RuleFor(x => x.StartTime)
            .Must(t => t.Hours is >= 0 and <= 23 && t.Minutes is >= 0 and <= 59)
            .WithMessage("StartTime hours must be 0–23 and minutes 0–59.");

        RuleFor(x => x.EndTime)
            .Must(t => t.Hours is >= 0 and <= 23 && t.Minutes is >= 0 and <= 59)
            .WithMessage("EndTime hours must be 0–23 and minutes 0–59.");

        RuleFor(x => x.ActualStartTime)
            .Must(t => t!.Hours is >= 0 and <= 23 && t.Minutes is >= 0 and <= 59)
            .WithMessage("ActualStartTime hours must be 0–23 and minutes 0–59.")
            .When(x => x.ActualStartTime != null);

        // "Work began at" only means something for a task that has begun. ApplyStatus clears the actual times
        // for NotStarted and Cancelled, so accepting one here would store a value the next status change drops
        // without saying so.
        RuleFor(x => x.ActualStartTime)
            .Must((x, _) => x.Status is PlannerTaskStatus.InProgress or PlannerTaskStatus.Completed)
            .WithMessage("ActualStartTime is only valid with status InProgress or Completed.")
            .When(x => x.ActualStartTime != null);

        RuleFor(x => x.Location)
            .MaximumLength(200)
            .When(x => x.Location != null);

        RuleFor(x => x.Notes)
            .MaximumLength(1000)
            .When(x => x.Notes != null);
    }
}