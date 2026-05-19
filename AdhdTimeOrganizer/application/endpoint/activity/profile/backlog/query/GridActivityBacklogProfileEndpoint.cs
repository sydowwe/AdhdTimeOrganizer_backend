using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.request.@base.table;
using AdhdTimeOrganizer.application.dto.response.activity.profile;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.mapper.activity.profile;
using AdhdTimeOrganizer.domain.model.entity.activity.profile;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activity.profile.backlog.query;

public class GridActivityBacklogProfileEndpoint(AppDbContext dbContext, ActivityBacklogProfileMapper mapper)
    : Endpoint<BaseFilterSortPaginateRequest<ActivityBacklogProfileFilterRequest>, BaseTableResponse<ActivityBacklogProfileResponse>>
{
    public override void Configure()
    {
        Post("/activity-backlog-profile/grid");
        Summary(s =>
        {
            s.Summary = "Get filtered and paginated ActivityBacklogProfile list";
            s.Response<BaseTableResponse<ActivityBacklogProfileResponse>>(200, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(BaseFilterSortPaginateRequest<ActivityBacklogProfileFilterRequest> req, CancellationToken ct)
    {
        try
        {
            var userId = User.GetId();

            var query = dbContext.Set<ActivityBacklogProfile>()
                .AsNoTracking()
                .Where(p => p.Activity.UserId == userId);

            if (req is { UseFilter: true, Filter: not null })
            {
                var filter = req.Filter;

                if (filter.EnergyLevel.HasValue)
                    query = query.Where(p => p.EnergyLevel == filter.EnergyLevel.Value);

                if (filter.EffortType.HasValue)
                    query = query.Where(p => p.EffortType == filter.EffortType.Value);

                if (filter.IsRepeatable.HasValue)
                    query = query.Where(p => p.IsRepeatable == filter.IsRepeatable.Value);
            }

            var response = await query.GetTableDataAsync<ActivityBacklogProfileResponse, ActivityBacklogProfile, ActivityBacklogProfileMapper>(
                req.SortBy,
                req.ItemsPerPage,
                req.Page,
                mapper,
                ct);

            await Send.OkAsync(response, ct);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error retrieving table data for ActivityBacklogProfile");
            AddError("An internal error occurred.");
            await Send.ErrorsAsync(500, ct);
        }
    }
}
