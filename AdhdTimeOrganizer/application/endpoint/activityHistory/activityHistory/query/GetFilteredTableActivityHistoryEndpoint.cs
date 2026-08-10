using AdhdTimeOrganizer.application.dto.filter.history;
using AdhdTimeOrganizer.application.dto.response.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.request.@base.table;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.activityHistory.query;

public class GetFilteredTableActivityHistoryEndpoint(AppDbContext dbContext)
    : Endpoint<BaseFilterSortPaginateRequest<ActivityHistoryFilterRequest>, List<ActivityHistoryListGroupedByDateResponse>>
{
    public virtual string EndpointPath => "gird";


    public override void Configure()
    {
        const string entityName = nameof(ActivityHistory);
        Post($"/{entityName.Kebaberize()}/{EndpointPath}");
        Summary(s =>
        {
            s.Summary = $"Get filtered and paginated {entityName} list";
            s.Description = $"Retrieves a filtered, paginated and sorted list of {entityName}";


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
                query = ApplyCustomFiltering(query, req.Filter);

            var history = await ActivityHistoryResponse.Projection(query)
                .GroupBy(hr => hr.StartTimestamp.ToUniversalTime().Date)
                .Select(group => new ActivityHistoryListGroupedByDateResponse
                {
                    Date = group.Key,
                    // Ensure IEnumerable<T> in final projection
                    HistoryResponseList = group.OrderBy(h => h.StartTimestamp).ToList()
                })
                .OrderBy(response => response.Date)
                .ToListAsync(ct);

            await Send.OkAsync(history, ct);
        }
        catch (Exception ex)
        {
            AddError($"An error occurred while retrieving filtered data: {ex.Message}");
            await Send.ErrorsAsync(500, ct);
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
            query = query.Where(ah => ah.ActivityId == filter.ActivityId.Value);

        if (filter.RoleId.HasValue)
            query = query.Where(h => h.Activity.RoleId == filter.RoleId);

        if (filter.CategoryId.HasValue)
            query = query.Where(h => h.Activity.CategoryId == filter.CategoryId);


        // These filter on to-do / routine membership via subqueries rather than Activity's inverse
        // collections, which were removed so Activity stops referencing the to-do and routine areas.
        // EF translates both forms to the same EXISTS; the Include() calls that used to sit here were
        // no-ops for filtering (Include never affects Where) and are gone with them.
        if (filter.IsFromTodoList.HasValue)
        {
            query = query.Where(ah => dbContext.TodoListItems.Any(tli => tli.ActivityId == ah.ActivityId)
                                      == filter.IsFromTodoList.Value);
            if (filter.TaskPriorityId.HasValue && filter.IsFromTodoList.Value)
                query = query.Where(ah => dbContext.TodoListItems.Any(tli => tli.ActivityId == ah.ActivityId
                                                                            && tli.TaskPriorityId == filter.TaskPriorityId.Value));
        }

        if (filter.IsFromRoutineTodoList.HasValue)
        {
            query = query.Where(ah => dbContext.RoutineTodoLists.Any(rtd => rtd.ActivityId == ah.ActivityId)
                                      == filter.IsFromRoutineTodoList.Value);
            if (filter.RoutineTimePeriodId.HasValue && filter.IsFromRoutineTodoList.Value)
                query = query.Where(ah => dbContext.RoutineTodoLists.Any(rtd => rtd.ActivityId == ah.ActivityId
                                                                               && rtd.TimePeriodId == filter.RoutineTimePeriodId.Value));
        }

        if (filter.IsUnavoidable.HasValue)
            query = query.Where(h => h.Activity.IsUnavoidable == filter.IsUnavoidable);

        if (filter.DateFrom.HasValue)
            query = query.Where(h => h.StartTimestamp >= filter.DateFrom);

        if (filter.DateTo.HasValue)
            query = query.Where(h => h.StartTimestamp <= filter.DateTo);

        if (filter.HoursBack.HasValue)
        {
            var cutoffTime = DateTime.UtcNow.AddHours(-filter.HoursBack.Value);
            query = query.Where(h => h.StartTimestamp >= cutoffTime);
        }

        if (filter.MinLength != null)
            query = query.Where(ah => ah.Length >= filter.MinLength);

        if (filter.MaxLength != null)
            query = query.Where(ah => ah.Length <= filter.MaxLength);

        return query;
    }
}