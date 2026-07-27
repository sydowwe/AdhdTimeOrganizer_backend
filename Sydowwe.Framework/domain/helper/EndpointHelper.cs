using Sydowwe.Framework.domain.result;

namespace Sydowwe.Framework.domain.helper;

public static class EndpointHelper
{
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

        ResultErrorType.BusinessRuleError => 422,

        ResultErrorType.InternalServerError => 500,
        ResultErrorType.DatabaseError => 500,
        ResultErrorType.UnknownError => 500,

        ResultErrorType.ExternalServiceError => 502,

        ResultErrorType.DbLockNotAvailableError => 503,
        ResultErrorType.DbDeadlockError => 503,

        _ => 400
    };
}