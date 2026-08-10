# Review: AdhdTimeOrganizer/infrastructure/extService/user/auth/GoogleSignInService.cs
Role: other (external auth service)
Summary: Google ID-token validation is sound (audience, signature, issuer via the library, explicit email_verified and expiry checks); account creation correctly defers to `UserRegistrationFlow.RunAsync` per convention, but the per-request `GoogleAuthorizationCodeFlow` is never disposed and exception handling collapses all failures (including malformed/tampered tokens) into `InternalServerError`.
Coverage: n/a

## Issues
- [Medium][Performance] GoogleSignInService.cs:17-25 — a new `GoogleAuthorizationCodeFlow` (which owns an `IHttpClientFactory`-backed `HttpClient` and implements `IDisposable` via `AuthorizationCodeFlow`) is constructed on every call and never disposed or wrapped in `using`.
  Why: under load this risks handle/socket exhaustion since each sign-in request creates and leaks its own HTTP client machinery instead of reusing a shared, injected one.
  Fix: wrap `flow` in a `using` statement, or better, inject a singleton `IGoogleAuthorizationCodeFlow`/reuse one `HttpClient` via `IHttpClientFactory`.
  Confidence: Med

- [Low][Quality] GoogleSignInService.cs:66-69 — the catch-all treats every failure (network error exchanging the code, a tampered/invalid ID token failing `ValidateAsync`, malformed payload) the same way, returning `ResultErrorType.InternalServerError`.
  Why: a client-supplied bad/expired/replayed `code` or forged token is a client error, not a server fault; conflating them muddies status-code semantics and monitoring/alerting (5xx spikes for what is actually abusive client input).
  Fix: catch `TokenResponseException` / `InvalidJwtException` (thrown by `GoogleJsonWebSignature.ValidateAsync` on signature/issuer/audience failure) separately and map to `BadRequest`, reserving `InternalServerError` for unexpected exceptions.
  Confidence: Med

- [Low][Quality] GoogleSignInService.cs:52-62 — `GoogleUserInfo.Name`, `.Picture`, and `.Locale` are declared `required` (non-nullable `string`) but assigned directly from `payload.Name` / `payload.Picture` / `payload.Locale`, which are optional OIDC claims Google does not guarantee on every token.
  Why: if Google omits these claims the properties silently hold `null` despite their non-nullable-looking type, which can surprise later code that dereferences them without a null check (currently unused by the registration path, so latent rather than active).
  Fix: make the DTO properties nullable (`string?`) to reflect reality, or default them to `string.Empty` at assignment.
  Confidence: Low

- [Nit][Quality] GoogleSignInService.cs:36-40 — the redundant manual expiry check (`payload.ExpirationTimeSeconds < UtcNow`) duplicates validation `GoogleJsonWebSignature.ValidateAsync` already performs internally.
  Why: not harmful, but suggests the validation contract of the library call isn't fully trusted/understood; worth a comment if intentional defense-in-depth.
  Fix: either remove as redundant or add a one-line comment explaining it's deliberate belt-and-braces.
  Confidence: Low

No PII/token logging present — good; the broad catch does not log `ex` or include the code/token in the swallowed message, and the endpoint (GoogleSignInEndpoint.cs) discards the detailed `Result.ErrorMessage` in favor of a generic client-facing message on this path, so no leak to the client either.
