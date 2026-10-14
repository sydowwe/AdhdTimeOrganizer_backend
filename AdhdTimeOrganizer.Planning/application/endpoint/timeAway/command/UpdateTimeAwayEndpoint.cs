using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.application.endpoint.extensions;
using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using FastEndpoints;
using Sydowwe.Framework.application.error;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.timeAway.command;

/// <summary>
/// <c>PUT /time-away/{id}</c> — rename, resize, move or re-kind a stretch. The days it held are un-stamped by what
/// the stamp wrote last time, and its new range stamped by what it says now, in the same transaction as the row: a
/// shrink releases the days that leave, a stretch joins the days that arrive, and a day the user overrode keeps its
/// override through all of it. Excused routine cycles are never re-scored.
/// </summary>
public class UpdateTimeAwayEndpoint(DbContext dbContext) : Endpoint<TimeAwayRequest, TimeAwayStampResponse>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Put("/time-away/{id:long}");
        Roles(AllowedRoles());
        Validator<TimeAwayValidator>();
        Summary(s =>
        {
            s.Summary = "Update a stretch of time away";
            s.Description =
                "Replaces the stretch's name, kind, dates, template, location and notes, and re-stamps its days: days "
                + "it no longer covers return to their own type, days it newly covers are stamped (and created when "
                + "missing). Answers the id, one entry per day of the new range, and the released dates.";
            s.Response<TimeAwayStampResponse>(200, "Updated");
            s.Response(400, "Invalid, or overlaps another stretch (errors.startDate names it)");
            s.Response(404, "No such stretch, or TemplateId names no template of this user's");
            s.Response(409, "Lost a concurrent write — re-read and retry");
        });
    }

    public override async Task HandleAsync(TimeAwayRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        var id = Route<long>("id");

        // Scoped by the host's global IEntityWithUser filter: another user's id is a 404 exactly like a missing one.
        var stretch = await dbContext.Set<TimeAway>().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (stretch is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (!await this.EnsureTimeAwayWritableAsync(dbContext, userId, req, exceptId: id, ct))
            return;

        TimeAwayStaging staging;
        try
        {
            // Before the mapping: what the stamp wrote is the old kind and location, and that is what un-stamps.
            var previous = TimeAwayStamp.Of(stretch);
            req.UpdateEntity(stretch);

            staging = await TimeAwayStamper.StageAsync(dbContext, stretch, previous, ct);
            await dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(this.ErrorStatus(result.ErrorType), ct);
            return;
        }

        if (staging.DayTypeChangedDates.Count > 0)
            await new CalendarDayTypesChangedEvent(userId, staging.DayTypeChangedDates).PublishAsync(Mode.WaitForAll, ct);

        await Send.OkAsync(TimeAwayStampResponse.From(stretch.Id, staging), ct);
    }
}
