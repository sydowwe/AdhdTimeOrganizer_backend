using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.android;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;
using Sydowwe.Framework.domain.@enum;

namespace AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.android;

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
    /// <summary>The role/category target's names, so a client can show the rule's target without a second lookup.</summary>
    public string? RoleName { get; set; }
    public string? CategoryName { get; set; }

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
            CategoryId = e.CategoryId,
            RoleName = e.Role == null ? null : e.Role.Name,
            CategoryName = e.Category == null ? null : e.Category.Name
        });
    }
}