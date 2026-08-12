using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.plannerTask.command;

/// <summary>
/// No reminder wiring here on purpose, and that is the opt-in guarantee working: a task that has just been
/// created cannot have a reminder yet — nothing can reference an id that did not exist a moment ago — and a
/// task never grows one on its own. The user attaches one afterwards by POSTing a <c>Reminder</c> with this
/// task's id, which is the only path that creates one.
/// </summary>
public class CreatePlannerTaskEndpoint(DbContext dbContext)
    : BaseCreateEndpoint<PlannerTask, PlannerTaskRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<PlannerTaskValidator>();
    }
}