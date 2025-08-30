namespace AdhdTimeOrganizer.domain.helper;

public static class EndpointHelper
{
    public static string[] GetUserOrHigherRoles()
    {
        return ["User", "Admin", "Root"];
    }

    public static string[] GetAdminOrHigherRoles()
    {
        return ["Admin", "Root"];
    }
}