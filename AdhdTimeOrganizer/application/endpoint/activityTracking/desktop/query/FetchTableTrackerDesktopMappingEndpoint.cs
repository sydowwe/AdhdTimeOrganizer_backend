using AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.application.dto.response.activityTracking.desktop;
using AdhdTimeOrganizer.application.endpoint.@base.read.pageFilterSort;
using AdhdTimeOrganizer.application.endpointGroups;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.mapper.activityTracking;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.desktop.query;

public class FetchTableTrackerDesktopMappingEndpoint(AppDbContext dbContext, TrackerDesktopMappingMapper mapper)
    : BaseFetchTableEndpoint<TrackerDesktopMappingByPattern, TrackerDesktopMappingResponse, TrackerDesktopMappingFilter, TrackerDesktopMappingMapper>(dbContext, mapper)
{
    public override void Configure()
    {
        base.Configure();
        Validator<FetchTableTrackerDesktopMappingValidator>();
        Group<ActivityTrackingDesktopSettingsGroup>();
    }

    protected override IQueryable<TrackerDesktopMappingByPattern> ApplyCustomFiltering(
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

    protected override IQueryable<TrackerDesktopMappingByPattern> WithIncludes(IQueryable<TrackerDesktopMappingByPattern> query)
    {
        return query.Include(e => e.Activity);
    }
}