using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.calendar;

public class GetByDateCalendarEndpoint(DbContext dbContext) : BaseGetByFieldEndpoint<Calendar, CalendarResponse>(dbContext)
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

        await base.HandleAsync(ct);
    }

    protected override IQueryable<Calendar> FilterByField(IQueryable<Calendar> query, string value)
    {
        var date = DateOnly.ParseExact(value, "dd-MM-yyyy");
        return query.Where(c => c.Date == date);
    }
}