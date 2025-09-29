using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.request.@base.table;
using AdhdTimeOrganizer.application.dto.response.activityHistory;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.activityHistory.query;

public class GetActivityHistoriesFilteredPaginatedEndpoint(
    AppCommandDbContext dbContext,
    ActivityHistoryMapper mapper)
    : Endpoint<BaseFilterSortPaginateRequest<ActivityHistoryFilterRequest>, List<ActivityHistoryListGroupedByDateResponse>>
{
    public virtual string EndpointPath => "filtered-table";

    public virtual string[] AllowedRoles()
    {
        return EndpointHelper.GetUserOrHigherRoles();
    }


    public override void Configure()
    {
        const string entityName = nameof(ActivityHistory);
        Post($"/{entityName.Kebaberize()}/{EndpointPath}");
        Summary(s =>
        {
            s.Summary = $"Get filtered and paginated {entityName} list";
            s.Description = $"Retrieves a filtered, paginated and sorted list of {entityName}";
            Roles(AllowedRoles());

            s.Response<List<ActivityHistoryListGroupedByDateResponse>>(200, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(BaseFilterSortPaginateRequest<ActivityHistoryFilterRequest> req, CancellationToken ct)
    {
        try
        {
            var query = WithIncludes(dbContext.ActivityHistories.AsNoTracking());

            query = query.FilteredByUser(User.GetId());

            if (req is { UseFilter: true, Filter: not null })
            {
                query = ApplyCustomFiltering(query, req.Filter);
            }

            var history = await mapper.ProjectToResponse(query)
                .GroupBy(hr => hr.StartTimestamp.ToUniversalTime().Date)
                .Select(group => new ActivityHistoryListGroupedByDateResponse
                {
                    Date = group.Key,
                    // Ensure IEnumerable<T> in final projection
                    HistoryResponseList = group.OrderBy(h => h.StartTimestamp).ToList()
                })
                .OrderBy(response => response.Date)
                .ToListAsync(ct);

            await SendOkAsync(history, ct);
        }
        catch (Exception ex)
        {
            AddError($"An error occurred while retrieving filtered data: {ex.Message}");
            await SendErrorsAsync(500, ct);
        }
    }

    protected IQueryable<ActivityHistory> WithIncludes(IQueryable<ActivityHistory> query)
    {
        return query
            .Include(ah => ah.Activity)
            .ThenInclude(a => a.Role)
            .Include(ah => ah.Activity)
            .ThenInclude(a => a.Category);
    }

    protected IQueryable<ActivityHistory> ApplyCustomFiltering(IQueryable<ActivityHistory> query, ActivityHistoryFilterRequest filter)
    {
        if (filter.ActivityId.HasValue)
        {
            query = query.Where(ah => ah.ActivityId == filter.ActivityId.Value);
        }

        if (filter.RoleId.HasValue)
            query = query.Where(h => h.Activity.CategoryId == filter.RoleId);

        if (filter.CategoryId.HasValue)
            query = query.Where(h => h.Activity.RoleId == filter.CategoryId);


        if (filter.IsFromTodoList.HasValue)
        {
            query = query.Include(ah => ah.Activity).ThenInclude(a => a.TodoList);

            query = query.Where(ah => ah.Activity.TodoList != null == filter.IsFromTodoList.Value);
            if (filter.TaskUrgencyId.HasValue && filter.IsFromTodoList.Value)
            {
                query = query.Where(ah => ah.Activity.TodoList!.TaskUrgencyId == filter.TaskUrgencyId.Value);
            }
        }

        if (filter.IsFromRoutineTodoList.HasValue)
        {
            query = query.Include(ah => ah.Activity).ThenInclude(a => a.RoutineTodoLists);

            query = query.Where(h => h.Activity.RoutineTodoLists.Any() == filter.IsFromRoutineTodoList.Value);
            if (filter.RoutineTimePeriodId.HasValue && filter.IsFromRoutineTodoList.Value)
            {
                query = query.Where(h => h.Activity.RoutineTodoLists.Any(rtd => rtd.TimePeriodId == filter.RoutineTimePeriodId.Value));
            }
        }

        if (filter.IsUnavoidable.HasValue)
        {
            query = query.Where(h => h.Activity.IsUnavoidable == filter.IsUnavoidable);
        }

        if (filter.DateFrom.HasValue)
        {
            query = query.Where(h => h.StartTimestamp >= filter.DateFrom);
        }

        if (filter.DateTo.HasValue)
        {
            query = query.Where(h => h.StartTimestamp <= filter.DateTo);
        }

        if (filter.HoursBack.HasValue)
        {
            var cutoffTime = DateTime.UtcNow.AddHours(-filter.HoursBack.Value);
            query = query.Where(h => h.StartTimestamp >= cutoffTime);
        }

        if (filter.MinLength != null)
        {
            query = query.Where(ah => ah.Length >= filter.MinLength);
        }

        if (filter.MaxLength != null)
        {
            query = query.Where(ah => ah.Length <= filter.MaxLength);
        }

        return query;
    }
}