using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.result;

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


    public static string[] GetUserOrHigherRoles() => ["User", "Admin", "Root"];

    public static string[] GetAdminOrHigherRoles() => ["Admin", "Root"];

    public static int ToStatusCode(ResultErrorType? errorType) => errorType switch
    {
        ResultErrorType.NotFound => 404,

        ResultErrorType.Conflict => 409,
        ResultErrorType.DbConcurrencyError => 409,
        ResultErrorType.DbUniqueViolationError => 409,
        ResultErrorType.DbForeignKeyError => 409,
        ResultErrorType.EmailHasPassword => 409,
        ResultErrorType.EmailHasGoogleOAuth => 409,

        ResultErrorType.AuthenticationFailed => 401,
        ResultErrorType.MissingInSession => 401,
        ResultErrorType.CookieMissing => 401,

        ResultErrorType.EmailNotConfirmed => 403,
        ResultErrorType.TwoFactorAuthRequired => 403,
        ResultErrorType.DbPermissionError => 403,

        ResultErrorType.UserLockedOut => 423,

        ResultErrorType.BussinessRuleError => 422,

        ResultErrorType.InternalServerError => 500,
        ResultErrorType.DatabaseError => 500,
        ResultErrorType.UnknownError => 500,

        ResultErrorType.ExternalServiceError => 502,

        ResultErrorType.DbLockNotAvailableError => 503,
        ResultErrorType.DbDeadlockError => 503,

        _ => 400
    };
}