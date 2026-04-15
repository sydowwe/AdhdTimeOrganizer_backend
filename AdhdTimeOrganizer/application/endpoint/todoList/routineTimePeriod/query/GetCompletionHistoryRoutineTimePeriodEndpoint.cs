using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.application.dto.response.toDoList;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTimePeriod.query;

public class GetCompletionHistoryRoutineTimePeriodEndpoint(AppDbContext dbContext)
    : Endpoint<IdRequest, List<PeriodCompletionRecord>>
{
    public override void Configure()
    {
        Get("/routine-time-period/{id}/completion-history");
        Roles(EndpointHelper.GetUserOrHigherRoles());

        Summary(s =>
        {
            s.Summary = "Get completion history for a routine time period";
            s.Description = "Returns the last HistoryDepth period completion records sorted oldest to newest";
            s.Response<List<PeriodCompletionRecord>>(200, "Success");
            s.Response(404, "Not found");
        });
    }

    public override async Task HandleAsync(IdRequest req, CancellationToken ct)
    {
        var loggedUserId = User.GetId();

        var period = await dbContext.Set<RoutineTimePeriod>()
            .Where(x => x.Id == req.Id && x.UserId == loggedUserId)
            .Select(x => new { x.Id, x.HistoryDepth })
            .FirstOrDefaultAsync(ct);

        if (period == null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var completions = await dbContext.RoutinePeriodCompletions
            .Where(c => c.TimePeriodId == period.Id)
            .OrderByDescending(c => c.PeriodStart)
            .Take(period.HistoryDepth)
            .Select(c => new PeriodCompletionRecord(c.PeriodStart, c.PeriodEnd, c.CompletedCount, c.TotalCount))
            .ToListAsync(ct);

        completions.Reverse();
        await SendOkAsync(completions, ct);
    }
}
