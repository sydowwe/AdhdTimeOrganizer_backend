using AdhdTimeOrganizer.Routines.application.dto.response.todoList;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineTimePeriod.query;

public class GetCompletionHistoryRoutineTimePeriodEndpoint(DbContext dbContext)
    : EndpointWithoutRequest<List<PeriodCompletionRecord>>
{
    public override void Configure()
    {
        Get("/routine-time-period/{id:long:required}/completion-history");


        Summary(s =>
        {
            s.Summary = "Get completion history for a routine time period";
            s.Description = "Returns the last HistoryDepth period completion records sorted oldest to newest";
            s.Response<List<PeriodCompletionRecord>>(200, "Success");
            s.Response(404, "Not found");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<long>("id");
        var loggedUserId = User.GetId();

        var period = await dbContext.Set<RoutineTimePeriod>()
            .Where(x => x.Id == id && x.UserId == loggedUserId)
            .Select(x => new { x.Id, x.HistoryDepth })
            .FirstOrDefaultAsync(ct);

        if (period == null)
        {
            AddError("RoutineTimePeriod not found.");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        var completions = await dbContext.Set<RoutinePeriodCompletion>()
            .Where(c => c.TimePeriodId == period.Id)
            .OrderByDescending(c => c.PeriodStart)
            .Take(period.HistoryDepth)
            .Select(c => new PeriodCompletionRecord(c.PeriodStart, c.PeriodEnd, c.CompletedCount, c.TotalCount, c.IsFrozen))
            .ToListAsync(ct);

        completions.Reverse();
        await Send.OkAsync(completions, ct);
    }
}