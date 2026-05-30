using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;

namespace AdhdTimeOrganizer.application.dto.response.activityTracking.desktop;

public record TrackerDesktopMappingResponse : IIdResponse, IProjectionResponse<TrackerDesktopMappingResponse, TrackerDesktopMappingByPattern>
{
    public long Id { get; init; }

    public string? ProcessName { get; set; }
    public PatternMatchType? ProcessNameMatchType { get; set; }

    public string? ProductName { get; set; }
    public PatternMatchType? ProductNameMatchType { get; set; }

    public string? WindowTitle { get; set; }
    public PatternMatchType? WindowTitleMatchType { get; set; }

    public bool IsActive { get; set; }

    public bool? IsIgnored { get; set; }
    public ActivityFilterFormResponse? Activity { get; set; }
    public long? RoleId { get; set; }
    public long? CategoryId { get; set; }

    public static IQueryable<TrackerDesktopMappingResponse> Projection(IQueryable<TrackerDesktopMappingByPattern> q) =>
        q.Select(e => new TrackerDesktopMappingResponse
        {
            Id = e.Id,
            ProcessName = e.ProcessName,
            ProcessNameMatchType = e.ProcessNameMatchType,
            ProductName = e.ProductName,
            ProductNameMatchType = e.ProductNameMatchType,
            WindowTitle = e.WindowTitle,
            WindowTitleMatchType = e.WindowTitleMatchType,
            IsActive = e.IsActive,
            IsIgnored = e.IsIgnored,
            Activity = e.Activity == null ? null : new ActivityFilterFormResponse { Id = e.Activity.Id, Text = e.Activity.Name, RoleId = e.Activity.RoleId, CategoryId = e.Activity.CategoryId },
            RoleId = e.RoleId,
            CategoryId = e.CategoryId,
        });

    public static TrackerDesktopMappingResponse FromEntity(TrackerDesktopMappingByPattern e) => new()
    {
        Id = e.Id,
        ProcessName = e.ProcessName,
        ProcessNameMatchType = e.ProcessNameMatchType,
        ProductName = e.ProductName,
        ProductNameMatchType = e.ProductNameMatchType,
        WindowTitle = e.WindowTitle,
        WindowTitleMatchType = e.WindowTitleMatchType,
        IsActive = e.IsActive,
        IsIgnored = e.IsIgnored,
        Activity = e.Activity == null ? null : new ActivityFilterFormResponse { Id = e.Activity.Id, Text = e.Activity.Name, RoleId = e.Activity.RoleId, CategoryId = e.Activity.CategoryId },
        RoleId = e.RoleId,
        CategoryId = e.CategoryId,
    };
}
