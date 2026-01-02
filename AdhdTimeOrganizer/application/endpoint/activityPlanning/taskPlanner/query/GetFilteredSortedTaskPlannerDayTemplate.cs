using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.dto.response.activityPlanning.taskPlannerDayTemplate;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPlannerDayTemplate.query;

public class GetFilterSortedPlannerTask(AppCommandDbContext dbContext, PlannerTaskMapper mapper)
    : BaseFilterEndpoint<PlannerTask, PlannerTaskResponse, PlannerTaskFilter, PlannerTaskMapper>(dbContext, mapper)
{
    protected override IQueryable<PlannerTask> ApplyCustomFiltering(IQueryable<PlannerTask> query, PlannerTaskFilter filter)
    {
        var untilIsNextDay = filter.Until.Hours <= filter.From.Hours;
        query = query.Where(t => t.CalendarId == filter.CalendarId);
        query = query.Where(task => task.StartTime >= new TimeOnly(filter.From.Hours, filter.From.Minutes));

        if (untilIsNextDay)
        {
            query = query.Where(task => task.EndTime <= new TimeOnly(23, 59, 59) || task.EndTime <= new TimeOnly(filter.Until.Hours, filter.Until.Minutes));
        }
        else
        {
            query = query.Where(task => task.EndTime <= new TimeOnly(filter.Until.Hours, filter.Until.Minutes));
        }

        return query;
    }

    public override SortByRequest[] DefaultSortBy =>
    [
        new()
        {
            Key = "StartTime",
            IsDesc = false
        }
    ];

    protected override IQueryable<PlannerTask> WithIncludes(IQueryable<PlannerTask> query)
    {
        return query
            .Include(pt => pt.Priority)
            .Include(pt => pt.Activity)
                .ThenInclude(a => a.Role)
            .Include(pt => pt.Activity)
                .ThenInclude(a => a.Category);
    }
}