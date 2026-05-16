using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class ApplyTemplateToTaskPlannerValidator : Validator<ApplyTemplateToTaskPlannerRequest>
{
    public ApplyTemplateToTaskPlannerValidator()
    {
        RuleFor(x => x.TemplateId).GreaterThan(0);
        RuleFor(x => x.CalendarId).GreaterThan(0);
        RuleFor(x => x.ConflictResolution).IsInEnum();
        RuleFor(x => x.TasksFromTemplate).NotNull().NotEmpty();

        RuleForEach(x => x.TasksFromTemplate)
            .ChildRules(task =>
            {
                task.RuleFor(t => t.StartTime)
                    .Must(t => t.Hours is >= 0 and <= 23 && t.Minutes is >= 0 and <= 59)
                    .WithMessage("StartTime hours must be 0–23 and minutes 0–59.");

                task.RuleFor(t => t.EndTime)
                    .Must(t => t.Hours is >= 0 and <= 23 && t.Minutes is >= 0 and <= 59)
                    .WithMessage("EndTime hours must be 0–23 and minutes 0–59.");

                task.RuleFor(t => t).Must(t => t.StartTime.ToTimeOnly() < t.EndTime.ToTimeOnly())
                    .WithMessage("StartTime must be before EndTime.");

                task.RuleFor(t => t.ActivityId).GreaterThan(0);
                task.RuleFor(t => t.CalendarId).GreaterThan(0);
                task.RuleFor(t => t.ImportanceId).GreaterThan(0).When(t => t.ImportanceId.HasValue);
            })
            .When(x => x.TasksFromTemplate != null);
    }
}
