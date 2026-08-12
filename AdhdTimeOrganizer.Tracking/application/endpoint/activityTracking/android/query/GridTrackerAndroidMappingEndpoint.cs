using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.android;
using AdhdTimeOrganizer.Tracking.application.endpointGroups;
using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.android;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.android.query;

public class GridTrackerAndroidMappingEndpoint(DbContext dbContext)
    : BaseGridEndpoint<TrackerAndroidMappingByPattern, TrackerAndroidMappingResponse, TrackerAndroidMappingFilter>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<FetchTableTrackerAndroidMappingValidator>();
        Group<ActivityTrackingAndroidSettingsGroup>();
    }

    protected override IQueryable<TrackerAndroidMappingByPattern> ApplyCustomFiltering(
        IQueryable<TrackerAndroidMappingByPattern> query, TrackerAndroidMappingFilter filter)
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

        if (filter is { PackageName: not null, PackageNameMatchType: not null })
            query = query.ApplyStringMatchFilter(e => e.PackageName, filter.PackageName, filter.PackageNameMatchType.Value);

        if (filter is { AppLabel: not null, AppLabelMatchType: not null })
            query = query.ApplyStringMatchFilter(e => e.AppLabel, filter.AppLabel, filter.AppLabelMatchType.Value);

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