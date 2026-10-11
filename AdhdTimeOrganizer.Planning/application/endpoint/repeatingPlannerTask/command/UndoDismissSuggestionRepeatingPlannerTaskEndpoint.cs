using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity.suggestion;
using FastEndpoints;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.repeatingPlannerTask.command;

/// <summary>
/// FO11. <c>POST /repeating-planner-task/suggestions/dismiss/undo</c> — the snackbar's Undo after a dismissal.
/// <para>
/// Deletes the dismissal row outright rather than restoring an earlier date. That is exact, not approximate: the
/// user could only tap "×" on a pattern that was showing, and a pattern only shows when any older dismissal of it
/// has lapsed — so whatever the row held before, it hid nothing. Idempotent: no row is still a 204.
/// </para>
/// </summary>
public class UndoDismissSuggestionRepeatingPlannerTaskEndpoint(DbContext dbContext) : Endpoint<DismissSuggestionRequest>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Post("/repeating-planner-task/suggestions/dismiss/undo");
        Roles(AllowedRoles());
        Validator<DismissSuggestionValidator>();
        Summary(s =>
        {
            s.Summary = "Undo a suggestion dismissal";
            s.Description = "Removes the dismissal of the pattern, so it is suggested again. A pattern with no dismissal is left as it is.";
            s.Response(204, "Undone, or there was nothing to undo");
            s.Response(400, "Not a DayOfWeek / DayOfMonth pattern, or the value is out of range");
        });
    }

    public override async Task HandleAsync(DismissSuggestionRequest req, CancellationToken ct)
    {
        // Scoped by the host's global IEntityWithUser filter, so another user's row (or activity) matches nothing.
        await dbContext.Set<PlannerSuggestionDismissal>()
            .Where(d => d.ActivityId == req.ActivityId
                        && d.PatternType == req.PatternType
                        && d.PatternValue == req.PatternValue)
            .ExecuteDeleteAsync(ct);

        await Send.NoContentAsync(ct);
    }
}
