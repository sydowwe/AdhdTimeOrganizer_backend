namespace Sydowwe.Framework.domain.result;

public enum ResultErrorType
{
    RecaptchaTokenInvalid,
    RecaptchaWrongAction,

    EmailHasPassword,
    EmailHasGoogleOAuth,
    AuthenticationFailed,
    UserLockedOut,
    EmailNotConfirmed,
    IdentityError,
    InternalServerError,
    TwoFactorAuthRequired,
    InvalidTwoFactorAuthToken,

    Unauthorized,


    FileUploadError,
    ExternalServiceError,
    MissingInSession,
    CookieMissing,
    JsonDeserializationError,

    MissingArgument,

    BadRequest,
    Conflict,

    BusinessRuleError,
    DbConcurrencyError,
    ValidationError,
    NotFound,
    DbUniqueViolationError,
    DbForeignKeyError,
    DbNullConstraintError,
    DbDeadlockError,
    DbLockNotAvailableError,
    DbPermissionError,
    DatabaseError,
    UnknownError,
    DeleteUnsuccessful,
    ExpectedSingleResult
}