namespace AdhdTimeOrganizer.Common.domain.result;

public enum ServiceErrorType
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

    CookieMissing,
    JsonDeserializationError,

    MissingArgument,

    NotFound,
    BadRequest,
    Conflict,
    ValidationError,
    ForeignKeyError,
    DatabaseError,
    UniqueViolationError,
    DbConcurrencyError
}