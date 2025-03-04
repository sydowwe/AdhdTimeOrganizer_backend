using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AdhdTimeOrganizer.Common.domain.helper;
using AdhdTimeOrganizer.Common.domain.result;
using Google.Apis.Auth;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;

namespace AdhdTimeOrganizer.Command.infrastructure.extService;

public static class GoogleSignInService
{
    public static async Task<ServiceResult<GoogleUserInfo>> GetUserInfoFromGoogleSignInCode(string code)
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
            var url = Helper.GetPageUri().ToString()[..^1];
            var tokenResponse = await flow.ExchangeCodeForTokenAsync(
                userId: "user",
                code: code,
                redirectUri: url,
                taskCancellationToken: CancellationToken.None
            );

            if (string.IsNullOrEmpty(tokenResponse.IdToken))
                return ServiceResult<GoogleUserInfo>.Error(ServiceErrorType.BadRequest, "Invalid Google login code");

            var payload = await GoogleJsonWebSignature.ValidateAsync(tokenResponse.IdToken);
            if (payload == null)
                return ServiceResult<GoogleUserInfo>.Error(ServiceErrorType.BadRequest, "Invalid token payload");

            var userInfo = new GoogleUserInfo
            {
                Email = payload.Email,
                UserId = payload.Subject,
            };

            return ServiceResult<GoogleUserInfo>.Successful(userInfo);
        }
        catch (Exception ex)
        {
            return ServiceResult<GoogleUserInfo>.Error(ServiceErrorType.InternalServerError, $"Google sign-in failed: {ex.Message}");
        }
    }
}

public class GoogleUserInfo
{
    public string Email { get; set; }
    public string UserId { get; set; }
    public string Locale { get; set; }
}