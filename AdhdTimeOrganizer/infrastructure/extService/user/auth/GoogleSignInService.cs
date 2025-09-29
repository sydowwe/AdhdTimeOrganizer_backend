using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.result;
using Google.Apis.Auth;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;

namespace AdhdTimeOrganizer.infrastructure.extService.user.auth;

public static class GoogleSignInService
{
    public static async Task<Result<GoogleUserInfo>> GetUserInfoFromGoogleSignInCode(string code)
    {
        try
        {
            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId =  Helper.GetEnvVar("OAUTH2_GOOGLE_CLIENT_ID"),
                    ClientSecret = Helper.GetEnvVar("OAUTH2_GOOGLE_CLIENT_SECRET")
                },
                Scopes = ["openid", "email"]
            });
            var url = Helper.GetPageUri().ToString();
            var tokenResponse = await flow.ExchangeCodeForTokenAsync(
                userId: "user",
                code: code,
                redirectUri: url,
                taskCancellationToken: CancellationToken.None
            );

            if (string.IsNullOrEmpty(tokenResponse.IdToken))
                return Result<GoogleUserInfo>.Error(ResultErrorType.BadRequest, "Invalid Google login code");

            var payload = await GoogleJsonWebSignature.ValidateAsync(tokenResponse.IdToken
            //     ,new GoogleJsonWebSignature.ValidationSettings{
            //     Audience = [Helper.GetEnvVar("OAUTH2_GOOGLE_CLIENT_ID")],
            //     HostedDomain = null // Set to your domain if you want to restrict to specific domains
            // }
                );

            if (payload == null)
                return Result<GoogleUserInfo>.Error(ResultErrorType.BadRequest, "Invalid token payload");

            // Additional security checks
            if (!payload.EmailVerified)
                return Result<GoogleUserInfo>.Error(ResultErrorType.BadRequest, "Email address not verified");

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
        catch (Exception ex)
        {
            return Result<GoogleUserInfo>.Error(ResultErrorType.InternalServerError, $"Google sign-in failed: {ex.Message}");
        }
    }
}

public class GoogleUserInfo
{
    public string Email { get; set; }
    public string UserId { get; set; }
    public string Name { get; set; }
    public string Picture { get; set; }
    public string Locale { get; set; }
    public bool EmailVerified { get; set; }
    public DateTime TokenIssuedAt { get; set; }
    public DateTime TokenExpiresAt { get; set; }
}