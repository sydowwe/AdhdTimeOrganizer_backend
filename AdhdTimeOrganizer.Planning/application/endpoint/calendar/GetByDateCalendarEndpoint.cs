using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using FastEndpoints;
using Sydowwe.Framework.application.endpoint.@base.read;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.calendar;

public class GetByDateCalendarEndpoint(DbContext dbContext, IPlannerStreakReader streakReader)
    : BaseGetByFieldEndpoint<Calendar, CalendarResponse>(dbContext)
{
    protected override string FieldName => nameof(Calendar.Date);

    public override async Task HandleAsync(CancellationToken ct)
    {
        var value = Route<string>("value");
        if (!DateOnly.TryParseExact(value, "dd-MM-yyyy", out _))
        {
            AddError("value", "Date must be in dd-MM-yyyy format.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        // Not base.HandleAsync: the base sends the response itself, and the streak has to be attached to it
        // first. It cannot come out of CalendarResponse.Projection — that runs per row, and a streak is a walk
        // across days.
        var day = await CalendarResponse
            .Projection(FilterByField(dbContext.Set<Calendar>().AsNoTracking(), value))
            .FirstOrDefaultAsync(ct);

        if (day is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        // Computed whatever date was asked for, and deliberately so: the streak is a fact about the user, not
        // about this day, so it reads the same from any date the client happens to open. IsTodayComplete is
        // always about the user's actual today — never about `date`.
        var streak = await streakReader.GetForUserAsync(User.GetId(), ct);

        await Send.OkAsync(day with { Streak = streak }, ct);
    }

    protected override IQueryable<Calendar> FilterByField(IQueryable<Calendar> query, string value)
    {
        var date = DateOnly.ParseExact(value, "dd-MM-yyyy");
        return query.Where(c => c.Date == date);
    }
}
