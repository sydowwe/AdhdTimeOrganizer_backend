using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Planning.application.validator;

public class ApplyTemplateToTaskPlannerValidator : Validator<ApplyTemplateToTaskPlannerRequest>
{
    public ApplyTemplateToTaskPlannerValidator()
    {
        RuleFor(x => x.TemplateId).GreaterThan(0);

        RuleFor(x => x)
            .Must(x => x.CalendarId.HasValue ^ x.Date.HasValue)
            .WithMessage("Provide exactly one of CalendarId or Date.");

        RuleFor(x => x.CalendarId).GreaterThan(0).When(x => x.CalendarId.HasValue);
        RuleFor(x => x.ConflictResolution).IsInEnum();
        RuleFor(x => x.TasksFromTemplate).NotNull().NotEmpty();

        RuleForEach(x => x.TasksFromTemplate)
            .ChildRules(task =>
            {
                task.RuleFor(t => t.StartTime).ApplyTimeOfDayRules();
                task.RuleFor(t => t.EndTime).ApplyTimeOfDayRules();

                task.RuleFor(t => t).Must(t => t.StartTime.IsBefore(t.EndTime))
                    .WithMessage("StartTime must be before EndTime.");

                task.RuleFor(t => t.ActivityId).GreaterThan(0);
                // Deliberately no rule on the nested CalendarId/Date: the endpoint stamps the one calendar it
                // resolved onto every task it builds. Requiring the client to repeat the id per task only ever
                // created the possibility of a template applied to two different days at once.
                task.RuleFor(t => t.ImportanceId).GreaterThan(0).When(t => t.ImportanceId.HasValue);
            })
            .When(x => x.TasksFromTemplate != null);
    }
}