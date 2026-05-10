using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.repeatingPlannerTask.query;

public class GetSuggestionsRepeatingPlannerTaskEndpoint(AppDbContext dbContext, RepeatingPlannerTaskMapper mapper)
    : EndpointWithoutRequest<List<RepeatingPlannerTaskResponse>>
{
    public override void Configure()
    {
        Get("/repeating-planner-task/suggestions");
        Roles(EndpointHelper.GetUserOrHigherRoles());
        Summary(s =>
        {
            s.Summary = "Get RepeatingPlannerTask suggestions for a date";
            s.Description = "Returns all active repeating tasks that match the given date based on recurrence type";
            s.Response<List<RepeatingPlannerTaskResponse>>(200, "Success");
            s.Response(400, "Invalid date format");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var dateString = Query<string>("date");
        if (!DateOnly.TryParseExact(dateString, "yyyy-MM-dd", out var date))
        {
            AddError("Invalid date format. Use yyyy-MM-dd.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        var userId = User.GetId();

        var tasks = await dbContext.Set<RepeatingPlannerTask>()
            .Where(t => t.UserId == userId && t.IsActive)
            .Include(t => t.Importance)
            .Include(t => t.Activity).ThenInclude(a => a.Role)
            .Include(t => t.Activity).ThenInclude(a => a.Category)
            .ToListAsync(ct);

        Calendar? calendar = null;
        if (tasks.Any(t => t.RecurrenceType == RecurrenceType.DayType))
        {
            calendar = await dbContext.Set<Calendar>()
                .FirstOrDefaultAsync(c => c.UserId == userId && c.Date == date, ct);
        }

        var suggestions = tasks
            .Where(t => t.RecurrenceType switch
            {
                RecurrenceType.DayOfWeek => t.ScheduledDays.Contains(date.DayOfWeek.ToString()),
                RecurrenceType.DayOfMonth => t.ScheduledDates.Contains(date.Day),
                RecurrenceType.DateRange => t.ActiveFromDate.HasValue && t.ActiveToDate.HasValue
                    && t.ActiveFromDate.Value <= date && date <= t.ActiveToDate.Value,
                RecurrenceType.DayType => calendar != null
                    && t.ScheduledForDayTypes.Contains(calendar.DayType.ToString()),
                _ => false
            })
            .Select(t => mapper.ToResponse(t))
            .ToList();

        await Send.OkAsync(suggestions, ct);
    }
}
