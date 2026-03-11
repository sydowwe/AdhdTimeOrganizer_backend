using AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.application.dto.request.@base.table;
using AdhdTimeOrganizer.application.dto.response.activityTracking.desktop;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.application.endpointGroups;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.mapper.activityTracking;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.desktop.query;

public class FetchTableTrackerDesktopMappingEndpoint(AppDbContext dbContext, TrackerDesktopMappingMapper mapper)
    : Endpoint<BaseFilterSortPaginateRequest<TrackerDesktopMappingFilter>, BaseTableResponse<TrackerDesktopMappingResponse>>
{
    public override void Configure()
    {
        Post("/tracker-desktop-mapping-by-pattern/filtered-table");
        Validator<FetchTableTrackerDesktopMappingValidator>();
        Roles(EndpointHelper.GetUserOrHigherRoles());
        Summary(s =>
        {
            s.Summary = "Get filtered and paginated TrackerDesktopMappingByPattern list";
            s.Response<BaseTableResponse<TrackerDesktopMappingResponse>>(200, "Success");
            s.Response(400, "Bad request");
        });
        Group<ActivityTrackingDesktopSettingsGroup>();
    }

    public override async Task HandleAsync(BaseFilterSortPaginateRequest<TrackerDesktopMappingFilter> req, CancellationToken ct)
    {
        var query = dbContext.TrackerDesktopMappingByPattern.AsNoTracking()
            .FilteredByUser(User.GetId());

        if (req is { UseFilter: true, Filter: not null })
            query = ApplyFilter(query, req.Filter);

        var result = await query.GetTableDataAsync<TrackerDesktopMappingResponse, TrackerDesktopMappingByPattern, TrackerDesktopMappingMapper>(req.SortBy, req.ItemsPerPage, req.Page, mapper, ct);

        await SendOkAsync(result, ct);
    }

    private static IQueryable<TrackerDesktopMappingByPattern> ApplyFilter(
        IQueryable<TrackerDesktopMappingByPattern> query, TrackerDesktopMappingFilter filter)
    {
        query = filter.Type switch
        {
            TrackerDesktopMappingTypeEnum.Ignored => query.Where(e => e.IsIgnored == true),
            TrackerDesktopMappingTypeEnum.Activity => query.Where(e => e.ActivityId.HasValue),
            TrackerDesktopMappingTypeEnum.Role => query.Where(e => e.RoleId.HasValue),
            TrackerDesktopMappingTypeEnum.Category => query.Where(e => e.CategoryId.HasValue),
            TrackerDesktopMappingTypeEnum.CategoryAndRole => query.Where(e => e.CategoryId.HasValue && e.RoleId.HasValue),
            _ => throw new ArgumentOutOfRangeException(nameof(filter.Type))
        };

        if (filter is { ProcessName: not null, ProcessNameMatchType: not null })
            query = query.ApplyStringMatchFilter(e => e.ProcessName, filter.ProcessName, filter.ProcessNameMatchType.Value);

        if (filter is { ProductName: not null, ProductNameMatchType: not null })
            query = query.ApplyStringMatchFilter(e => e.ProductName, filter.ProductName, filter.ProductNameMatchType.Value);

        if (filter is { WindowTitle: not null, WindowTitleMatchType: not null })
            query = query.ApplyStringMatchFilter(e => e.WindowTitle, filter.WindowTitle, filter.WindowTitleMatchType.Value);

        if (filter.IsActive.HasValue)
            query = query.Where(e => e.IsActive == filter.IsActive.Value);

        if (filter.IsIgnored.HasValue)
            query = query.Where(e => e.IsIgnored == filter.IsIgnored.Value);

        if (filter.ActivityId.HasValue)
            query = query.Where(e => e.ActivityId == filter.ActivityId.Value);

        if (filter.RoleId.HasValue)
            query = query.Where(e => e.RoleId == filter.RoleId.Value);

        if (filter.CategoryId.HasValue)
            query = query.Where(e => e.CategoryId == filter.CategoryId.Value);

        return query;
    }
}