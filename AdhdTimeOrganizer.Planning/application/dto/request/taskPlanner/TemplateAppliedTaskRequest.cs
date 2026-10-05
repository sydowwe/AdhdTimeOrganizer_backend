namespace AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;

/// <summary>
/// One line of an apply-template request: a planner task, plus the template line it was built from.
/// <para>
/// Its own type rather than a field on <see cref="PlannerTaskRequest"/>, because the origin is written
/// <b>only</b> by an apply. On the plain create it would let any task claim a template it never came from, and on
/// the full-replace PUT every existing caller omits it, so reading the omission would wipe the origin on the
/// first edit.
/// </para>
/// </summary>
public record TemplateAppliedTaskRequest : PlannerTaskRequest
{
    /// <summary>
    /// The <c>TemplatePlannerTask</c> this task was built from. Kept only when it is a line of the template being
    /// applied — see <c>PlannerTemplateApplier.BuildTasks</c>. Null is a task the user added to the preview.
    /// </summary>
    public long? SourceTemplateTaskId { get; init; }
}
