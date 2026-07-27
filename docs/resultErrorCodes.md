# Result Error Codes → HTTP Status

Shared reference for every module. `ResultErrorType`
(`Sydowwe.Framework/domain/result/ResultErrorType.cs`) is the error channel of the
`Result` / `Result<T>` pattern. The **authoritative** enum→HTTP mapping is
`EndpointHelper.ToStatusCode(ResultErrorType?)`
(`Sydowwe.Framework/domain/helper/EndpointHelper.cs`) — when in doubt, that switch
wins over this doc.

## How to use it

In an endpoint, after an operation returns a failed `Result`, translate the error
type with the central helper instead of hand-rolling a `switch`:

```csharp
if (result.Failed)
{
    AddError(result.ErrorMessage!);
    await Send.ErrorsAsync(EndpointHelper.ToStatusCode(result.ErrorType), ct);
    return;
}
```

The framework `Base*Endpoint`s already do this. Hand-written endpoints should use
`EndpointHelper.ToStatusCode` too, so status codes stay consistent across modules.
(Several existing hand-rolled endpoints map their own subset — e.g. some map
`ValidationError → 422`; the convention below is `ValidationError → 400`. Prefer the
helper.)

## Mapping table

| HTTP | `ResultErrorType` | Typical cause |
|---|---|---|
| **400** Bad Request | `BadRequest` | Generic client error |
| | `ValidationError` | Failed domain validation; also DB **check-constraint**, string-too-long, numeric-out-of-range (mapped by `DbUtils.HandleException`) |
| | `MissingArgument` | Required argument absent |
| | `JsonDeserializationError` | Malformed request body |
| | `RecaptchaTokenInvalid` / `RecaptchaWrongAction` | reCAPTCHA verification failed |
| | `IdentityError` | ASP.NET Identity operation error |
| | `InvalidTwoFactorAuthToken` | Bad 2FA token |
| | `FileUploadError` | Upload handling failed |
| | `DbNullConstraintError` | NOT NULL violation (from `DbUtils`) |
| | `DeleteUnsuccessful` | Delete affected no rows |
| | `ExpectedSingleResult` | Query expected exactly one row |
| | *(any unmapped value)* | falls through to **400** (`_ => 400`) |
| **401** Unauthorized | `AuthenticationFailed` | Bad credentials / not signed in |
| | `MissingInSession` | Required session state absent |
| | `CookieMissing` | Required cookie absent |
| **403** Forbidden | `EmailNotConfirmed` | Account email not yet confirmed |
| | `TwoFactorAuthRequired` | 2FA required to proceed |
| | `DbPermissionError` | Insufficient DB privilege (from `DbUtils`) |
| **404** Not Found | `NotFound` | Entity / route target missing |
| **409** Conflict | `Conflict` | Business conflict (e.g. insufficient stock) |
| | `DbConcurrencyError` | `row_version` mismatch / `DbUpdateConcurrencyException` |
| | `DbUniqueViolationError` | Unique-index violation (from `DbUtils`) |
| | `DbForeignKeyError` | FK violation (from `DbUtils`) |
| | `EmailHasPassword` / `EmailHasGoogleOAuth` | Account already exists with that auth method |
| **422** Unprocessable Entity | `BussinessRuleError` | Domain rule rejected the request (note enum spelling: `Bussiness`) |
| **423** Locked | `UserLockedOut` | Account locked after failed attempts |
| **500** Internal Server Error | `InternalServerError` | Unhandled server fault |
| | `DatabaseError` | Unclassified DB update error (from `DbUtils`) |
| | `UnknownError` | Unexpected non-DB exception (from `DbUtils`) |
| **502** Bad Gateway | `ExternalServiceError` | Upstream/third-party call failed |
| **503** Service Unavailable | `DbLockNotAvailableError` | Lock could not be acquired (from `DbUtils`) |
| | `DbDeadlockError` | Deadlock detected (from `DbUtils`) |

## Where the DB ones come from

`DbUtils.HandleException`
(`Sydowwe.Framework/infrastructure/persistence/DatabaseExceptionUtils.cs`) is what
the `DbContextHelper` CRUD helpers call to turn a Postgres exception into a typed
`ResultErrorType`. Notable Postgres `SqlState` → error-type mappings:

| Postgres `SqlState` | `ResultErrorType` | → HTTP |
|---|---|---|
| `UniqueViolation` (23505) | `DbUniqueViolationError` | 409 |
| `ForeignKeyViolation` (23503) | `DbForeignKeyError` | 409 |
| `NotNullViolation` (23502) | `DbNullConstraintError` | 400 |
| `CheckViolation` (23514) | `ValidationError` | 400 |
| `StringDataRightTruncation` (22001) | `ValidationError` | 400 |
| `NumericValueOutOfRange` (22003) | `ValidationError` | 400 |
| `DeadlockDetected` (40P01) | `DbDeadlockError` | 503 |
| `LockNotAvailable` (55P03) | `DbLockNotAvailableError` | 503 |
| `InsufficientPrivilege` (42501) | `DbPermissionError` | 403 |
| `DbUpdateConcurrencyException` | `DbConcurrencyError` | 409 |
| *(any other PG / update error)* | `DatabaseError` | 500 |
| *(non-DB exception)* | `UnknownError` | 500 |

> A duplicate-key insert therefore surfaces as **409**, not 400 — keep that in mind
> when asserting on unique-constraint behaviour in tests.
