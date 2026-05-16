using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class PlannerTaskValidator : Validator<PlannerTaskRequest>
{
    public PlannerTaskValidator()
    {
        RuleFor(x => x.ActivityId)
            .GreaterThan(0);

        RuleFor(x => x.ImportanceId)
            .GreaterThan(0)
            .When(x => x.ImportanceId.HasValue);

        RuleFor(x => x.CalendarId)
            .GreaterThan(0);

        RuleFor(x => x.TodolistId)
            .GreaterThan(0)
            .When(x => x.TodolistId.HasValue);

        RuleFor(x => x.StartTime)
            .Must(t => t.Hours is >= 0 and <= 23 && t.Minutes is >= 0 and <= 59)
            .WithMessage("StartTime hours must be 0–23 and minutes 0–59.");

        RuleFor(x => x.EndTime)
            .Must(t => t.Hours is >= 0 and <= 23 && t.Minutes is >= 0 and <= 59)
            .WithMessage("EndTime hours must be 0–23 and minutes 0–59.");

        RuleFor(x => x.Location)
            .MaximumLength(200)
            .When(x => x.Location != null);

        RuleFor(x => x.Notes)
            .MaximumLength(1000)
            .When(x => x.Notes != null);
    }
}
