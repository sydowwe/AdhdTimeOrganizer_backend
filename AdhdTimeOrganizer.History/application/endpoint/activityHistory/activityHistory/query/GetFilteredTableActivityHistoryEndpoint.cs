using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.History.application.dto.filter.history;
using AdhdTimeOrganizer.History.application.dto.response.activityHistory;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.request.@base.table;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.History.application.endpoint.activityHistory.activityHistory.query;

public class GetFilteredTableActivityHistoryEndpoint(
    DbContext dbContext,
    IEnumerable<IActivityMembershipSource> membershipSources)
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
            var query = WithIncludes(dbContext.Set<ActivityHistory>().AsNoTracking());

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


        // To-do / routine membership goes through Core's IActivityMembershipSource seam rather than
        // naming TodoListItem / RoutineTodoList directly - that is what keeps History's only project
        // reference pointing at Core, and what lets these three slices be extracted in any order.
        // Each source hands back a composable IQueryable<long> of activity ids, so Contains() renders
        // as IN (SELECT ...) - the same plan the hand-written EXISTS subqueries produced.
        query = ApplyMembershipFilter(query, ActivityMembershipSourceKeys.TodoList,
            filter.IsFromTodoList, filter.TaskPriorityId);

        query = ApplyMembershipFilter(query, ActivityMembershipSourceKeys.RoutineTodoList,
            filter.IsFromRoutineTodoList, filter.RoutineTimePeriodId);

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

    /// <summary>
    /// Narrows <paramref name="query"/> to history rows whose activity is (or is not) a member of the
    /// source registered under <paramref name="key"/>.
    /// </summary>
    /// <remarks>
    /// A missing source means the slice that owns it is not installed in this host, in which case the
    /// filter is skipped rather than treated as "nothing matches" - a filter that silently empties the
    /// grid is far worse than one that silently widens it. Every slice this portal ships registers its
    /// source, so in practice the null branch is unreachable here.
    /// <para>
    /// When <paramref name="facetId"/> is set on a positive match, the facet-narrowed set is the
    /// membership set - the broad "is a member at all" check would be redundant, so only the narrow
    /// one is applied. On a negative match the facet is meaningless and ignored, matching the old
    /// behaviour.
    /// </para>
    /// </remarks>
    private IQueryable<ActivityHistory> ApplyMembershipFilter(
        IQueryable<ActivityHistory> query, string key, bool? isMember, long? facetId)
    {
        if (!isMember.HasValue)
            return query;

        var source = membershipSources.FirstOrDefault(s => s.Key == key);
        if (source is null)
            return query;

        if (isMember.Value)
        {
            var ids = source.ActivityIds(dbContext, facetId);
            return query.Where(ah => ids.Contains(ah.ActivityId));
        }

        var allIds = source.ActivityIds(dbContext, null);
        return query.Where(ah => !allIds.Contains(ah.ActivityId));
    }
}