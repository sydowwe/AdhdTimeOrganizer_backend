using AdhdTimeOrganizer.Tracking.domain.model.@enum;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.desktop;
using AdhdTimeOrganizer.Tracking.application.endpointGroups;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.desktop.query;

public class GridTrackerDesktopMappingEndpoint(DbContext dbContext)
    : BaseGridEndpoint<TrackerDesktopMappingByPattern, TrackerDesktopMappingResponse, TrackerDesktopMappingFilter>(dbContext)
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
            // No target shape asked for: every mapping, still narrowed by whatever else the filter sets.
            null => query,
            TrackerMappingTypeEnum.Ignored => query.Where(e => e.IsIgnored == true),
            TrackerMappingTypeEnum.Activity => query.Where(e => e.ActivityId.HasValue),
            TrackerMappingTypeEnum.Role => query.Where(e => e.RoleId.HasValue),
            TrackerMappingTypeEnum.Category => query.Where(e => e.CategoryId.HasValue),
            TrackerMappingTypeEnum.CategoryAndRole => query.Where(e => e.CategoryId.HasValue && e.RoleId.HasValue),
            // Unreachable for a value a client can send — the validator's IsInEnum rule makes an undefined
            // one a 400 (CQ-15). What is left is a member added to the enum and not handled here, and a
            // 500 is the right answer to that: the alternative is a page silently unnarrowed.
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