using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Planning.application.validator;

public class CarryOverPlannerTaskValidator : Validator<CarryOverPlannerTaskRequest>
{
    public CarryOverPlannerTaskValidator()
    {
        // The "after the task's current day" half needs the row, so it is checked in the endpoint.
        RuleFor(x => x.Date).NotEmpty();
    }
}
