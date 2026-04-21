using AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.application.dto.request.@base.table;
using AdhdTimeOrganizer.application.dto.response.activityTracking.desktop;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.endpointGroups;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.desktop.query;

public class FetchCategoriesAndRolesByPattern(AppDbContext dbContext)
    : Endpoint<BaseFilterSortPaginateRequest<TrackerDesktopDistinctEntriesFilter>, DesktopCategoriesAndRolesByPattern>
{
    public override void Configure()
    {
        const string entityName = "Distinct desktop entries";
        Post("/fetch-categories-and-roles-by-pattern");
        Roles(EndpointHelper.GetUserOrHigherRoles());
        Summary(s =>
        {
            s.Summary = $"Get filtered and paginated {entityName} list";
            s.Description = $"Retrieves a filtered, paginated and sorted list of {entityName}";
            s.Response<DesktopCategoriesAndRolesByPattern>(200, "Success");
            s.Response(400, "Bad request");
        });
        Group<ActivityTrackingDesktopGroup>();
    }

    public override async Task HandleAsync(BaseFilterSortPaginateRequest<TrackerDesktopDistinctEntriesFilter> req, CancellationToken ct)
    {
        try
        {
            var query = dbContext.TrackerDesktopMappingByPattern.AsNoTracking();

            query = query.FilteredByUser(User.GetId());

            if (req is { UseFilter: true, Filter: not null })
            {
                query = ApplyCustomFiltering(query, req.Filter);
            }

            var rows = await query
                .Select(e => new
                {
                    e.CategoryId,
                    CategoryName = e.Category != null ? e.Category.Name : null,
                    e.RoleId,
                    RoleName = e.Role != null ? e.Role.Name : null
                })
                .ToListAsync(ct);

            var categories = rows
                .Where(e => e.CategoryId != null)
                .Select(e => new SelectOptionResponse(e.CategoryId!.Value, e.CategoryName!))
                .DistinctBy(e => e.Id)
                .ToList();

            var roles = rows
                .Where(e => e.RoleId != null)
                .Select(e => new SelectOptionResponse(e.RoleId!.Value, e.RoleName!))
                .DistinctBy(e => e.Id)
                .ToList();

            await Send.OkAsync(new DesktopCategoriesAndRolesByPattern
            {
                Categories = categories,
                Roles = roles
            }, ct);
        }
        catch (Exception ex)
        {
            AddError($"An error occurred while retrieving filtered data: {ex.Message}");
            await Send.ErrorsAsync(500, ct);
        }
    }

    private static IQueryable<TrackerDesktopMappingByPattern> ApplyCustomFiltering(IQueryable<TrackerDesktopMappingByPattern> query, TrackerDesktopDistinctEntriesFilter filter)
    {
        if (filter is { ProcessName: not null, ProcessNameMatchType: not null })
            query = query.ApplyStringMatchFilter(e => e.ProcessName, filter.ProcessName, filter.ProcessNameMatchType ?? PatternMatchType.Exact);

        if (filter is { ProductName: not null, ProductNameMatchType: not null })
            query = query.ApplyStringMatchFilter(e => e.ProductName, filter.ProductName, filter.ProductNameMatchType ?? PatternMatchType.Exact);

        if (filter is { WindowTitle: not null, WindowTitleMatchType: not null })
            query = query.ApplyStringMatchFilter(e => e.WindowTitle, filter.WindowTitle, filter.WindowTitleMatchType.Value);

        return query;
    }
}