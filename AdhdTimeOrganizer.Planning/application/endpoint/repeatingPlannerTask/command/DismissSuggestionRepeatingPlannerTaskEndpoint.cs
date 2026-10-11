using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity.suggestion;
using FastEndpoints;
using Sydowwe.Framework.application.error;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.repeatingPlannerTask.command;

/// <summary>
/// FO11. <c>POST /repeating-planner-task/suggestions/dismiss</c> — "×, not any more" on a pattern suggestion.
/// <para>
/// Upserts one <see cref="PlannerSuggestionDismissal"/> per (activity, pattern type, pattern value), dated today, so
/// a repeat dismissal restarts the window rather than adding a row. From then on
/// <c>GetSuggestionsRepeatingPlannerTaskEndpoint</c> drops that pattern from both pattern tiers until the
/// dismissal lapses (<see cref="domain.service.SuggestionPatternWindow"/>).
/// </para>
/// <para>
/// It does not check that such a pattern exists. The row would be invisible either way, and requiring one would
/// race the view's refresh: a pattern the user just saw can drop out of the view between the read and the click.
/// </para>
/// </summary>
public class DismissSuggestionRepeatingPlannerTaskEndpoint(
    DbContext dbContext,
    IReferencedIdOwnershipService ownership,
    TimeProvider timeProvider) : Endpoint<DismissSuggestionRequest>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Post("/repeating-planner-task/suggestions/dismiss");
        Roles(AllowedRoles());
        Validator<DismissSuggestionValidator>();
        Summary(s =>
        {
            s.Summary = "Stop suggesting a pattern";
            s.Description = "Hides the DayOfWeek or DayOfMonth pattern for the activity from the day planner's suggestions. "
                            + "The dismissal lapses after the pattern's window (12 weeks for a weekday, 12 months for a day "
                            + "of the month), after which the pattern is offered again only if it has re-formed since.";
            s.Response(204, "Dismissed");
            s.Response(400, "Not a DayOfWeek / DayOfMonth pattern, or the value is out of range");
            s.Response(404, "Activity not found");
        });
    }

    public override async Task HandleAsync(DismissSuggestionRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        if (!await ownership.OwnsActivityAsync(dbContext, userId, req.ActivityId, ct))
        {
            AddError(r => r.ActivityId, "Activity not found.");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        try
        {
            // Scoped by the host's global IEntityWithUser filter.
            var dismissal = await dbContext.Set<PlannerSuggestionDismissal>()
                .FirstOrDefaultAsync(d => d.ActivityId == req.ActivityId
                                          && d.PatternType == req.PatternType
                                          && d.PatternValue == req.PatternValue, ct);

            if (dismissal is null)
            {
                dbContext.Set<PlannerSuggestionDismissal>().Add(new PlannerSuggestionDismissal
                {
                    UserId = userId,
                    ActivityId = req.ActivityId,
                    PatternType = req.PatternType,
                    PatternValue = req.PatternValue,
                    DismissedOn = today
                });
            }
            else
            {
                dismissal.DismissedOn = today;
            }

            await dbContext.SaveChangesAsync(ct);
            await Send.NoContentAsync(ct);
        }
        catch (Exception ex)
        {
            // Two tabs dismissing the same row at once lose on the unique index: a 409, not a 500.
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(this.ErrorStatus(result.ErrorType), ct);
        }
    }
}
