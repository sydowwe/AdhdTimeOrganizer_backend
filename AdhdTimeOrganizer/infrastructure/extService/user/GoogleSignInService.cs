using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using Google.Apis.Auth;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.domain.result;

namespace AdhdTimeOrganizer.infrastructure.extService.user.auth;

public class GoogleSignInService : IGoogleSignInService, IScopedService
{
    public async Task<Result<GoogleUserInfo>> GetUserInfoFromGoogleSignInCode(string code)
    {
        try
        {
            using var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = Helper.GetEnvVar("OAUTH2_GOOGLE_CLIENT_ID"),
                    ClientSecret = Helper.GetEnvVar("OAUTH2_GOOGLE_CLIENT_SECRET")
                },
                Scopes = ["openid", "email"]
            });
            var tokenResponse = await flow.ExchangeCodeForTokenAsync(
                "user",
                code,
                "postmessage",
                CancellationToken.None
            );

            if (string.IsNullOrEmpty(tokenResponse.IdToken))
                return Result<GoogleUserInfo>.Error(ResultErrorType.BadRequest, "Invalid Google login code");

            var payload = await GoogleJsonWebSignature.ValidateAsync(tokenResponse.IdToken,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = [Helper.GetEnvVar("OAUTH2_GOOGLE_CLIENT_ID")]
                });

            if (payload == null)
                return Result<GoogleUserInfo>.Error(ResultErrorType.BadRequest, "Invalid token payload");

            // Additional security checks
            if (!payload.EmailVerified)
                return Result<GoogleUserInfo>.Error(ResultErrorType.BadRequest, "Email address not verified");

            // Belt-and-braces: GoogleJsonWebSignature.ValidateAsync already rejects an expired token
            // internally, but expiry is cheap to re-check and this keeps the check from silently
            // depending on the library's internals never changing.
            if (payload.ExpirationTimeSeconds < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                return Result<GoogleUserInfo>.Error(ResultErrorType.Unauthorized, "Token has expired");

            var userInfo = new GoogleUserInfo
            {
                Email = payload.Email,
                UserId = payload.Subject,
                Name = payload.Name,
                Picture = payload.Picture,
                Locale = payload.Locale,
                EmailVerified = payload.EmailVerified,
                TokenIssuedAt = DateTimeOffset.FromUnixTimeSeconds(payload.IssuedAtTimeSeconds ?? 0).DateTime,
                TokenExpiresAt = DateTimeOffset.FromUnixTimeSeconds(payload.ExpirationTimeSeconds ?? 0).DateTime
            };

            return Result<GoogleUserInfo>.Successful(userInfo);
        }
        catch (Exception ex) when (ex is TokenResponseException or InvalidJwtException)
        {
            // A rejected code exchange or a tampered/invalid/expired ID token is bad client input,
            // not a server fault — keep it out of 5xx alerting.
            return Result<GoogleUserInfo>.Error(ResultErrorType.BadRequest, "Google sign-in failed");
        }
        catch (Exception ex)
        {
            return Result<GoogleUserInfo>.Error(ResultErrorType.InternalServerError, $"Google sign-in failed: {ex.Message}");
        }
    }
}

public class GoogleUserInfo
{
    public required string Email { get; set; }
    public required string UserId { get; set; }
    public string? Name { get; set; }
    public string? Picture { get; set; }
    public string? Locale { get; set; }
    public required bool EmailVerified { get; set; }
    public required DateTime TokenIssuedAt { get; set; }
    public required DateTime TokenExpiresAt { get; set; }
}