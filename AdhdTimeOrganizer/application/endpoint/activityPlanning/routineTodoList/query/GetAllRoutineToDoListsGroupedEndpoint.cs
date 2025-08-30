
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.routineTodoList.query;

public class GetAllRoutineTodoListsGroupedEndpoint(
    AppCommandDbContext dbContext,
    RoutineTimePeriodMapper routineTimePeriodMapper,
    RoutineTodoListMapper mapper) : EndpointWithoutRequest<IEnumerable<RoutineTodoListGroupedResponse>>
{
    public override void Configure()
    {
        Get("/routine-todo-list/grouped-by-time-period");
        Roles(EndpointHelper.GetUserOrHigherRoles());

        Summary(s =>
        {
            s.Summary = "Get all routine todo lists grouped by time period";
            s.Description = "Retrieves all routine todo lists grouped by their time period";
            s.Response<IEnumerable<RoutineTodoListGroupedResponse>>(200, "Success");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var loggedUserId = User.GetId();

        var data = await dbContext.Set<RoutineTimePeriod>()
            .GroupJoin(
                dbContext.Set<RoutineTodoList>().Where(x => x.UserId == loggedUserId),
                tp => tp.Id,
                rtd => rtd.TimePeriodId,
                (tp, items) => new
                {
                    TimePeriod = tp,
                    Items = items
                })
            .Select(x => new RoutineTodoListGroupedResponse
            {
                RoutineTimePeriod = routineTimePeriodMapper.ToResponse(x.TimePeriod),
                Items = x.Items.Select(mapper.ToResponse)
            })
            .ToListAsync(ct);

        await SendOkAsync(data, ct);
    }
}
