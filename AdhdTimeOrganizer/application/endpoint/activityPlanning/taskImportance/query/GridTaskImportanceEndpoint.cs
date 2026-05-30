using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.application.endpoint.@base.read.pageFilterSort;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskImportance.query;

public class GridTaskImportanceEndpoint(
    AppDbContext dbContext)
    : BaseGridEndpoint<TaskImportance, TaskImportanceResponse, TaskImportanceFilterRequest>(dbContext)
{
    protected override IQueryable<TaskImportance> ApplyCustomFiltering(IQueryable<TaskImportance> query, TaskImportanceFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            query = query.Where(tu => tu.Text != null && tu.Text.Contains(filter.Text));
        }

        if (!string.IsNullOrWhiteSpace(filter.Color))
        {
            query = query.Where(tu => tu.Color == filter.Color);
        }

        if (filter.MinImportance.HasValue)
        {
            query = query.Where(tu => tu.Importance >= filter.MinImportance.Value);
        }

        if (filter.MaxImportance.HasValue)
        {
            query = query.Where(tu => tu.Importance <= filter.MaxImportance.Value);
        }

        return query;
    }
}
