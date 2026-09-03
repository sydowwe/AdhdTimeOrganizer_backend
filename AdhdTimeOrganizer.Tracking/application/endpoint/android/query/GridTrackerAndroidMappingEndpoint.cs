using AdhdTimeOrganizer.Tracking.domain.model.@enum;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.android;
using AdhdTimeOrganizer.Tracking.application.endpointGroups;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.android;
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
        // The same enum as the desktop grid, and now named like it (CQ-15): both mapping tables carry the
        // same target group, so there is one type rather than a copy per table.
        query = filter.Type switch
        {
            null => query,
            TrackerMappingTypeEnum.Ignored => query.Where(e => e.IsIgnored == true),
            TrackerMappingTypeEnum.Activity => query.Where(e => e.ActivityId.HasValue),
            TrackerMappingTypeEnum.Role => query.Where(e => e.RoleId.HasValue),
            TrackerMappingTypeEnum.Category => query.Where(e => e.CategoryId.HasValue),
            TrackerMappingTypeEnum.CategoryAndRole => query.Where(e => e.CategoryId.HasValue && e.RoleId.HasValue),
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