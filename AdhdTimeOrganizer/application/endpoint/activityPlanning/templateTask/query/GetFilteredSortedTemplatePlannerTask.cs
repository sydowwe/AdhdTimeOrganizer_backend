using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.application.dto.response.activityPlanning.taskPlannerDayTemplate;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPlannerDayTemplate.query;

public class GetFilterSortedTemplatePlannerTask(AppCommandDbContext dbContext, TemplatePlannerTaskMapper mapper)
    : BaseFilterEndpoint<TemplatePlannerTask, TemplatePlannerTaskResponse, TemplatePlannerTaskFilter, TemplatePlannerTaskMapper>(dbContext, mapper)
{
    protected override IQueryable<TemplatePlannerTask> ApplyCustomFiltering(IQueryable<TemplatePlannerTask> query, TemplatePlannerTaskFilter filter)
    {
        var untilIsNextDay = filter.Until.Hours <= filter.From.Hours;
        query = query.Where(t => t.TemplateId == filter.TemplateId);
        query = query.Where(task => task.StartTime >= new TimeOnly(filter.From.Hours, filter.From.Minutes));

        if (untilIsNextDay)
        {
            query = query.Where(task => task.EndTime <= new TimeOnly(23, 59,59) || task.EndTime <= new TimeOnly(filter.Until.Hours, filter.Until.Minutes));
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

    protected override IQueryable<TemplatePlannerTask> WithIncludes(IQueryable<TemplatePlannerTask> query)
    {
        return query.Include(t => t.Activity);
    }
}