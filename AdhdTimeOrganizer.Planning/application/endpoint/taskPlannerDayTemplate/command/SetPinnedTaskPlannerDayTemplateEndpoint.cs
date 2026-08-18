using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner.template;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using FastEndpoints;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.taskPlannerDayTemplate.command;

/// <summary>
/// Pins / unpins one template for the calling user. The pin is a column on the template, so the row's own
/// user scoping is the authorization: the global query filter on <c>IEntityWithUser</c> makes another
/// user's template a 404 here, exactly as it does on update and delete.
/// </summary>
public class SetPinnedTaskPlannerDayTemplateEndpoint(DbContext dbContext)
    : BasePatchEndpoint<TaskPlannerDayTemplate, SetPinnedTaskPlannerDayTemplateRequest>(dbContext)
{
    /// <summary>
    /// The base's route is the generic <c>/task-planner-day-template/{id}</c> patch route; this endpoint owns
    /// one field, so it takes a suffixed route and leaves the generic one free for a real patch later.
    /// </summary>
    public override void Configure()
    {
        Patch("/task-planner-day-template/{id:long}/pinned");
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = "Pin or unpin a TaskPlannerDayTemplate";
            s.Description = "Sets whether the template is pinned to the top of the caller's template list.";
            s.Response(204, "Success");
            s.Response(404, "Not found");
            s.Response(400, "Bad request");
        });
    }

    protected override void Mapping(TaskPlannerDayTemplate entity, SetPinnedTaskPlannerDayTemplateRequest req)
    {
        entity.IsPinned = req.IsPinned;
    }
}
