using FastEndpoints;
using FastEndpoints.Swagger;

namespace AdhdTimeOrganizer.application.endpointGroups;

public class ActivityTrackingGroup : Group
{
    public ActivityTrackingGroup() => Configure("/activity-tracking", ep => ep.Description(x => x.AutoTagOverride("ActivityTracking")));
}

public class ActivityTrackingDesktopGroup : Group
{
    public ActivityTrackingDesktopGroup() => Configure("/activity-tracking/desktop", ep => ep.Description(x => x.AutoTagOverride("ActivityTracking/Desktop")));
}

public class ActivityTrackingDesktopSettingsGroup : Group
{
    public ActivityTrackingDesktopSettingsGroup() => Configure("/activity-tracking/desktop/settings", ep => ep.Description(x => x.AutoTagOverride("ActivityTracking/Desktop/Settings")));
}