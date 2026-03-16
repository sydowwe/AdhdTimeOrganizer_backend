using AdhdTimeOrganizer.application.dto.response.generic;

namespace AdhdTimeOrganizer.application.dto.response.activityTracking.desktop;

public record DesktopCategoriesAndRolesByPattern
{
    public required IEnumerable<SelectOptionResponse> Categories { get; set; }
    public required IEnumerable<SelectOptionResponse> Roles { get; set; }
}