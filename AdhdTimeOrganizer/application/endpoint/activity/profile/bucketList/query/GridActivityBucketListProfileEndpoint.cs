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

namespace AdhdTimeOrganizer.application.endpoint.activity.profile.bucketList.query;

public class GridActivityBucketListProfileEndpoint(AppDbContext dbContext, ActivityBucketListProfileMapper mapper)
    : Endpoint<BaseFilterSortPaginateRequest<ActivityBucketListProfileFilterRequest>, BaseTableResponse<ActivityBucketListProfileResponse>>
{
    public override void Configure()
    {
        Post("/activity-bucket-list-profile/grid");
        Summary(s =>
        {
            s.Summary = "Get filtered and paginated ActivityBucketListProfile list";
            s.Response<BaseTableResponse<ActivityBucketListProfileResponse>>(200, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(BaseFilterSortPaginateRequest<ActivityBucketListProfileFilterRequest> req, CancellationToken ct)
    {
        try
        {
            var userId = User.GetId();

            var query = dbContext.Set<ActivityBucketListProfile>()
                .AsNoTracking()
                .Where(p => p.Activity.UserId == userId);

            if (req is { UseFilter: true, Filter: not null })
            {
                var filter = req.Filter;

                if (filter.RequiresTravel.HasValue)
                    query = query.Where(p => p.RequiresTravel == filter.RequiresTravel.Value);

                if (filter.ComfortZoneStep.HasValue)
                    query = query.Where(p => p.ComfortZoneStep == filter.ComfortZoneStep.Value);
            }

            var response = await query.GetTableDataAsync<ActivityBucketListProfileResponse, ActivityBucketListProfile, ActivityBucketListProfileMapper>(
                req.SortBy,
                req.ItemsPerPage,
                req.Page,
                mapper,
                ct);

            await Send.OkAsync(response, ct);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error retrieving table data for ActivityBucketListProfile");
            AddError("An internal error occurred.");
            await Send.ErrorsAsync(500, ct);
        }
    }
}
