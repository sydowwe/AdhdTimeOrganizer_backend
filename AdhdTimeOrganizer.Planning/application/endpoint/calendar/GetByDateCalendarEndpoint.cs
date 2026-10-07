using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using Sydowwe.Framework.application.endpoint.@base.read;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Planning.application.endpoint.calendar;

/// <summary>
/// <c>GET /calendar/by-Date/{dd-MM-yyyy}</c> — the day planner's first read of a day; its tasks follow through
/// <c>planner-task/filter</c>, keyed on the id this returns.
/// <para>
/// <b>A GET that writes (FO6),</b> exactly as <c>GET /calendar/day-plan/{date}</c> does: for the user's today and
/// tomorrow, auto-placing repeating rules are materialised first, so a user who opens the planner before home
/// still finds the day's blocks. It may create the calendar row, so a day that would have been a 404 can come back
/// as a 200 holding only the placed blocks. Never fails the read.
/// </para>
/// </summary>
public class GetByDateCalendarEndpoint(
    DbContext dbContext,
    IPlannerStreakReader streakReader,
    IRepeatingTaskMaterialiser materialiser)
    : BaseGetByFieldEndpoint<Calendar, CalendarResponse>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;
    protected override string FieldName => nameof(Calendar.Date);

    public override async Task HandleAsync(CancellationToken ct)
    {
        var value = Route<string>("value");
        if (!DateOnly.TryParseExact(value, "dd-MM-yyyy", out var date))
        {
            AddError("value", "Date must be in dd-MM-yyyy format.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        // Before the read, since it commits and may create the very row read below. The client fetches the
        // tasks only after this answers, so they include what was just placed.
        await materialiser.PlaceForReadAsync(User.GetId(), date, ct);

        // Not base.HandleAsync: the base sends the response itself, and the streak has to be attached to it
        // first. It cannot come out of CalendarResponse.Projection — that runs per row, and a streak is a walk
        // across days.
        var day = await CalendarResponse
            .Projection(FilterByField(_dbContext.Set<Calendar>().AsNoTracking(), value))
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