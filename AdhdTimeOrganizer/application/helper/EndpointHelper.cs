using AdhdTimeOrganizer.domain.model.entity.user;

namespace AdhdTimeOrganizer.application.helper;

public static class EndpointHelper
{
    /// <summary>
    /// Gets the verified user from HttpContext.
    /// This user has already passed password and 2FA verification.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if user is not verified.</exception>
    public static User GetVerifiedUser(this HttpContext context)
    {
        return context.Items["VerifiedUser"] as User
               ?? throw new InvalidOperationException("User not verified. Ensure VerifyUserPreProcessor is configured.");
    }

    public static string[] GetUserOrHigherRoles()
    {
        return ["User", "Admin", "Root"];
    }

    public static string[] GetAdminOrHigherRoles()
    {
        return ["Admin", "Root"];
    }
}