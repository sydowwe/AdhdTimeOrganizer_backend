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
/// <c>POST /time-away</c> — mark a stretch of time away once: the stretch and the stamp on every day of its range,
/// in one transaction (<see cref="TimeAwayStamper"/>), creating the days that do not exist yet.
/// <para>
/// Not <c>BaseCreateEndpoint</c>, which answers the bare id: this answers what the stamp did to each day. The
/// exception mapping is the base's.
/// </para>
/// </summary>
public class CreateTimeAwayEndpoint(DbContext dbContext) : Endpoint<TimeAwayRequest, TimeAwayStampResponse>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Post("/time-away");
        Roles(AllowedRoles());
        Validator<TimeAwayValidator>();
        Summary(s =>
        {
            s.Summary = "Mark a stretch of time away";
            s.Description =
                "Creates a named stretch of Vacation or SickDay days and stamps every day of its inclusive range, "
                + "creating days that have none. A day takes the stretch's kind unless it carries a type of the "
                + "user's own (Special, or the other away kind), which it keeps; the stretch's location fills days "
                + "with none. The template is only suggested on these days, never applied. Answers the new id and "
                + "one entry per day.";
            s.Response<TimeAwayStampResponse>(201, "Created");
            s.Response(400, "Invalid, or overlaps another stretch (errors.startDate names it)");
            s.Response(404, "TemplateId names no template of this user's");
            s.Response(409, "Lost a concurrent write to one of the days — retry");
        });
    }

    public override async Task HandleAsync(TimeAwayRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        if (!await this.EnsureTimeAwayWritableAsync(dbContext, userId, req, exceptId: null, ct))
            return;

        var stretch = req.ToEntity;
        stretch.UserId = userId;

        TimeAwayStaging staging;
        try
        {
            dbContext.Set<TimeAway>().Add(stretch);
            staging = await TimeAwayStamper.StageAsync(dbContext, stretch, previous: null, ct);
            await dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var result = DbUtils.HandleException(ex, "Create");
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(this.ErrorStatus(result.ErrorType), ct);
            return;
        }

        // After the commit: Routines excuses the closed cycles the stretch covers, and FO6 withdraws an untouched
        // auto-placed block from today or tomorrow. Both handlers log and swallow, so this cannot fail the request.
        if (staging.DayTypeChangedDates.Count > 0)
            await new CalendarDayTypesChangedEvent(userId, staging.DayTypeChangedDates).PublishAsync(Mode.WaitForAll, ct);

        await Send.ResponseAsync(TimeAwayStampResponse.From(stretch.Id, staging), 201, ct);
    }
}
