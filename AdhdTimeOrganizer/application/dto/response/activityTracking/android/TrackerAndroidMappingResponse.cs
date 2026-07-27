using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.android;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;
using Sydowwe.Framework.domain.@enum;

namespace AdhdTimeOrganizer.application.dto.response.activityTracking.android;

public record TrackerAndroidMappingResponse : IIdResponse, IProjectionResponse<TrackerAndroidMappingResponse, TrackerAndroidMappingByPattern>
{
    public long Id { get; init; }

    public string? PackageName { get; set; }
    public PatternMatchType? PackageNameMatchType { get; set; }

    public string? AppLabel { get; set; }
    public PatternMatchType? AppLabelMatchType { get; set; }

    public bool IsActive { get; set; }

    public bool? IsIgnored { get; set; }
    public ActivityFilterFormResponse? Activity { get; set; }
    public long? RoleId { get; set; }
    public long? CategoryId { get; set; }

    public static IQueryable<TrackerAndroidMappingResponse> Projection(IQueryable<TrackerAndroidMappingByPattern> q)
    {
        return q.Select(e => new TrackerAndroidMappingResponse
        {
            Id = e.Id,
            PackageName = e.PackageName,
            PackageNameMatchType = e.PackageNameMatchType,
            AppLabel = e.AppLabel,
            AppLabelMatchType = e.AppLabelMatchType,
            IsActive = e.IsActive,
            IsIgnored = e.IsIgnored,
            Activity = e.Activity == null ? null : new ActivityFilterFormResponse { Id = e.Activity.Id, Text = e.Activity.Name, RoleId = e.Activity.RoleId, CategoryId = e.Activity.CategoryId },
            RoleId = e.RoleId,
            CategoryId = e.CategoryId
        });
    }
}