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

namespace AdhdTimeOrganizer.application.endpoint.activity.profile.project.query;

public class GridActivityProjectProfileEndpoint(AppDbContext dbContext, ActivityProjectProfileMapper mapper)
    : Endpoint<BaseFilterSortPaginateRequest<ActivityProjectProfileFilterRequest>, BaseTableResponse<ActivityProjectProfileResponse>>
{
    public override void Configure()
    {
        Post("/activity-project-profile/grid");
        Summary(s =>
        {
            s.Summary = "Get filtered and paginated ActivityProjectProfile list";
            s.Response<BaseTableResponse<ActivityProjectProfileResponse>>(200, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(BaseFilterSortPaginateRequest<ActivityProjectProfileFilterRequest> req, CancellationToken ct)
    {
        try
        {
            var userId = User.GetId();

            var query = dbContext.Set<ActivityProjectProfile>()
                .AsNoTracking()
                .Where(p => p.Activity.UserId == userId);

            if (req is { UseFilter: true, Filter: not null })
            {
                var filter = req.Filter;

                if (filter.DifficultyLevel.HasValue)
                    query = query.Where(p => p.DifficultyLevel == filter.DifficultyLevel.Value);

                if (filter.ReadinessStatus.HasValue)
                    query = query.Where(p => p.ReadinessStatus == filter.ReadinessStatus.Value);

                if (filter.IsMessy.HasValue)
                    query = query.Where(p => p.IsMessy == filter.IsMessy.Value);

                if (!string.IsNullOrWhiteSpace(filter.ProjectArea))
                    query = query.Where(p => p.ProjectArea.Contains(filter.ProjectArea));
            }

            var response = await query.GetTableDataAsync<ActivityProjectProfileResponse, ActivityProjectProfile, ActivityProjectProfileMapper>(
                req.SortBy,
                req.ItemsPerPage,
                req.Page,
                mapper,
                ct);

            await Send.OkAsync(response, ct);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error retrieving table data for ActivityProjectProfile");
            AddError("An internal error occurred.");
            await Send.ErrorsAsync(500, ct);
        }
    }
}
